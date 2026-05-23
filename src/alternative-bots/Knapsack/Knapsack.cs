using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

/// <summary>
/// KnapsackBot — milih target kayak problem knapsack, yang paling "untung" yang duluan.
///
/// Rasio = Value / Cost
///   → Value: seberapa worth musuh ini dihabisi (HP rendah = hampir mati = nilai tinggi)
///   → Cost: estimasi energi yang kita buang buat ngabisin dia
///   → yang rasionya paling gede = target paling efisien buat ditembak sekarang.
///
/// Fire power juga pake logika knapsack — jangan overspend buat musuh yang hampir mati.
/// Safe mode kalau energi udah tipis: orbit jauh, tembak hemat.
/// </summary>
public class KnapsackBot : Bot
{
    const double SAFE_ENERGY = 25;
    const double WALL_MARGIN = 80;
    const double CLOSE_RANGE = 220;
    const int MEMORY_TURNS = 12;

    class Enemy { public double X, Y, Energy, Speed, Heading; public int LastSeen; }
    readonly Dictionary<int, Enemy> enemies = new();
    int radarDir = 1, moveSign = 1, lastFlip = 0;

    static void Main(string[] args) => new KnapsackBot().Start();
    KnapsackBot() : base(BotInfo.FromFile("Knapsack.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(0x22, 0x8B, 0x22); // forest green
        TurretColor = Color.FromArgb(0xFF, 0x00, 0x00); // merah
        RadarColor = Color.FromArgb(0xFF, 0xD7, 0x00); // kuning radar
        ScanColor = Color.FromArgb(0xFF, 0xFF, 0x00); // kuning scan
        BulletColor = Color.FromArgb(0xFF, 0x00, 0x00); // merah

        AdjustGunForBodyTurn = AdjustRadarForBodyTurn = AdjustRadarForGunTurn = true;
        MaxSpeed = 8; MaxTurnRate = 10; MaxGunTurnRate = 20; MaxRadarTurnRate = 45;

        while (IsRunning) { Tick(); Go(); }
    }

    void Tick()
    {
        // buang data musuh yang udah kedaluwarsa atau udah mati
        foreach (var id in enemies.Where(p => TurnNumber - p.Value.LastSeen > MEMORY_TURNS || p.Value.Energy <= 0)
                                  .Select(p => p.Key).ToList())
            enemies.Remove(id);

        var target = KnapsackPickTarget();
        Radar(target);
        Shoot(target);
        Move(target);
    }

    // pilih musuh dengan rasio value/cost terbaik — inti dari logika knapsack
    Enemy KnapsackPickTarget()
    {
        if (!enemies.Any()) return null;

        Enemy best = null; double bestRatio = double.NegativeInfinity;
        foreach (var e in enemies.Values)
        {
            if (TurnNumber - e.LastSeen > MEMORY_TURNS || e.Energy <= 0) continue;

            double dist = DistanceTo(e.X, e.Y);
            if (dist > 1200) continue;

            // value: seberapa worth musuh ini dihabisi sekarang
            double killValue = Math.Max(0, 100.0 - e.Energy);
            double hitChance = 1.0 - Math.Min(1.0, dist / 800.0);
            double value = killValue * hitChance
                             + (e.Energy < 20 ? 40 : 0)
                             + (e.Energy < 8 ? 60 : 0)
                             + (dist < CLOSE_RANGE ? 25 : 0);

            // cost: estimasi energi yang kita habisin buat ngabisin dia
            double bulletsNeeded = Math.Max(1, e.Energy / 4.0);
            double powerPerShot = dist < CLOSE_RANGE ? 3.0 : dist < 400 ? 2.2 : 1.5;
            double cost = bulletsNeeded * powerPerShot;

            double ratio = value / Math.Max(1, cost);

            // kalau energi kita udah tipis, musuh mahal jadi jauh lebih ga worth
            if (Energy < SAFE_ENERGY) ratio *= (1.0 / Math.Max(1, cost)) * 10.0;

            if (ratio > bestRatio) { bestRatio = ratio; best = e; }
        }
        return best;
    }

    // tembak dengan power yang pas — jangan buang energi lebih dari yang perlu
    void Shoot(Enemy target)
    {
        if (target == null || TurnNumber - target.LastSeen > 4 || target.Energy <= 0)
        { GunTurnRate = 0; return; }

        double dist = DistanceTo(target.X, target.Y);

        // power dipilih seefisien mungkin buat kondisi saat ini
        double pwr;
        if (Energy < SAFE_ENERGY)
            pwr = target.Energy < 4 ? 0.5 : 0.8;
        else if (target.Energy < 4)
            pwr = Math.Min(target.Energy + 0.1, 3.0); // jangan overspend buat yang hampir mati
        else
            pwr = dist < CLOSE_RANGE ? 3.0 : dist < 400 ? 2.2 : dist < 600 ? 1.5 : 1.0;

        double t = dist / CalcBulletSpeed(pwr);
        double px = Math.Clamp(target.X + Math.Sin(ToRad(target.Heading)) * target.Speed * t, 18, ArenaWidth - 18);
        double py = Math.Clamp(target.Y + Math.Cos(ToRad(target.Heading)) * target.Speed * t, 18, ArenaHeight - 18);
        double gb = GunBearingTo(px, py);
        GunTurnRate = Math.Clamp(gb, -20, 20);

        double tol = dist < 200 ? 8 : dist < 400 ? 5.0 : 3.0;
        if (GunHeat == 0 && Math.Abs(gb) <= tol && Energy > pwr + 0.5 && dist <= 750)
            SetFire(pwr);
    }

    // radar ngikutin target, kalau ga ada target muter aja
    void Radar(Enemy target)
    {
        if (target == null) { RadarTurnRate = 45 * radarDir; return; }
        double b = RadarBearingTo(target.X, target.Y);
        RadarTurnRate = Math.Clamp(b * 2.0, -45, 45);
        if (Math.Abs(b) < 2) radarDir *= -1;
    }

    // gerak agresif kalau sehat, orbit aman kalau energi tipis
    void Move(Enemy target)
    {
        if (target == null) { Patrol(); return; }
        if (Energy < SAFE_ENERGY) { SafeOrbit(target); return; }

        if (TurnNumber - lastFlip > 30) { moveSign *= -1; lastFlip = TurnNumber; }

        double eDir = DirectionTo(target.X, target.Y);

        // kejar kalau HP musuh udah mau abis, strafe kalau masih sehat
        (double Dir, double Spd)[] candidates = target.Energy < 15
            ? new[] { (eDir, 8.0), (eDir + 15, 7.0), (eDir - 15, 7.0) }
            : new[] { (eDir + 90, 8.0), (eDir - 90, 8.0), (eDir + 110, 6.0), (eDir - 110, 6.0) };

        var best = candidates[0]; double bestS = double.NegativeInfinity;
        foreach (var c in candidates)
        {
            double rx = X + Math.Sin(ToRad(c.Dir)) * c.Spd * 12;
            double ry = Y + Math.Cos(ToRad(c.Dir)) * c.Spd * 12;
            double wall = Math.Min(Math.Min(rx, ry), Math.Min(ArenaWidth - rx, ArenaHeight - ry));
            double s = 2.5 * (wall < WALL_MARGIN ? -4.0 : 1.0)
                        + 2.0 * (1.0 - Math.Min(1.0, Math.Abs(DistanceTo(rx, ry) - 250) / 400.0));
            if (s > bestS) { bestS = s; best = c; }
        }
        double brg = CalcBearing(NormalizeAbsoluteAngle(best.Dir));
        TurnRate = Math.Clamp(brg, -10, 10);
        TargetSpeed = (Math.Abs(brg) > 100 ? -best.Spd : best.Spd) * moveSign;
    }

    // orbit jauh biar susah kena tembak kalau lagi sekarat
    void SafeOrbit(Enemy target)
    {
        double eDir = DirectionTo(target.X, target.Y);
        double dist = DistanceTo(target.X, target.Y);
        double orbitDir = eDir + (dist < 400 ? 120 : 90);
        double brg = CalcBearing(NormalizeAbsoluteAngle(orbitDir));
        TurnRate = Math.Clamp(brg, -10, 10);
        TargetSpeed = 7 * moveSign;
        if (TurnNumber - lastFlip > 25) { moveSign *= -1; lastFlip = TurnNumber; }
    }

    // kalau ga ada target, jalan-jalan dulu sambil nunggu radar nangkep sesuatu
    void Patrol()
    {
        bool nearWall = X < WALL_MARGIN || Y < WALL_MARGIN
                     || X > ArenaWidth - WALL_MARGIN || Y > ArenaHeight - WALL_MARGIN;
        TurnRate = nearWall ? Math.Clamp(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0), -10, 10) : 6;
        TargetSpeed = 6;
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        if (!IsTeammate(e.ScannedBotId))
            enemies[e.ScannedBotId] = new Enemy
            {
                X = e.X,
                Y = e.Y,
                Energy = e.Energy,
                Speed = e.Speed,
                Heading = e.Direction,
                LastSeen = TurnNumber
            };
    }

    public override void OnBotDeath(BotDeathEvent e) { enemies.Remove(e.VictimId); }
    public override void OnHitWall(HitWallEvent e) { moveSign *= -1; lastFlip = TurnNumber; }
    public override void OnHitByBullet(HitByBulletEvent e) { moveSign *= -1; lastFlip = TurnNumber; }
    public override void OnHitBot(HitBotEvent e) { moveSign *= -1; TargetSpeed = -6; }
    public override void OnSkippedTurn(SkippedTurnEvent e) { RadarTurnRate = 45; GunTurnRate = TurnRate = 0; TargetSpeed = 4; }

    static double ToRad(double d) => d * Math.PI / 180.0;
}