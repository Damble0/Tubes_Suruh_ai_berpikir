using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class GreedyBot : Bot
{
    const double WEIGHT_ENERGY = 1.0, WEIGHT_SCORE = 0.5;
    const double MIN_FIRE_POWER = 1.0, MAX_FIRE_POWER = 3.0, CLOSE_RANGE = 150.0;
    const double AGGRO_THRESHOLD = 40.0;
    const double WALL_MARGIN = 80;
    const int MEMORY_TURNS = 12;

    class Enemy { public double X, Y, Energy, Speed, Heading; public int LastSeen; }
    readonly Dictionary<int, Enemy> enemies = new();
    int radarDir = 1, moveSign = 1, lastFlip = 0;

    static void Main(string[] args) => new GreedyBot().Start();
    GreedyBot() : base(BotInfo.FromFile("Greedy.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(0x00, 0xFF, 0xFF); // cyan
        TurretColor = Color.FromArgb(0xFF, 0xFF, 0x00); // kuning
        RadarColor = Color.FromArgb(0xFF, 0x69, 0xB4); // pink
        ScanColor = Color.FromArgb(0xFF, 0xC0, 0xCB); // pink muda
        BulletColor = Color.FromArgb(0x00, 0xFF, 0x00); // ijo

        AdjustGunForBodyTurn = AdjustRadarForBodyTurn = AdjustRadarForGunTurn = true;
        MaxSpeed = 8; MaxTurnRate = 10; MaxGunTurnRate = 20; MaxRadarTurnRate = 45;

        while (IsRunning) { Tick(); Go(); }
    }

    void Tick()
    {
        // buang data musuh yang udah basi atau udah mati
        foreach (var id in enemies.Where(p => TurnNumber - p.Value.LastSeen > MEMORY_TURNS || p.Value.Energy <= 0)
                                  .Select(p => p.Key).ToList())
            enemies.Remove(id);

        var target = GreedyPickTarget();
        Radar(target);
        Shoot(target);
        Move(target);
    }

    // pilih musuh dengan skor greedy tertinggi — energi tinggi + skor tinggi = prioritas
    Enemy GreedyPickTarget()
    {
        Enemy best = null; double bestScore = double.NegativeInfinity;
        foreach (var e in enemies.Values)
        {
            if (TurnNumber - e.LastSeen > MEMORY_TURNS || e.Energy <= 0) continue;
            double score = e.Energy * WEIGHT_ENERGY + e.Energy * WEIGHT_SCORE;
            if (score > bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    // arahin laras ke prediksi posisi musuh, tembak kalau udah pas
    void Shoot(Enemy target)
    {
        if (target == null || TurnNumber - target.LastSeen > 4 || target.Energy <= 0)
        { GunTurnRate = 0; return; }

        double dist = DistanceTo(target.X, target.Y);
        double pwr = IsAggressive() ? MAX_FIRE_POWER
                    : dist < CLOSE_RANGE ? MAX_FIRE_POWER
                    : dist < 400 ? 2.0 : MIN_FIRE_POWER;

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

    // gerak strafe ke kiri/kanan musuh, balik arah tiap 30 turn biar ga ketebak
    void Move(Enemy target)
    {
        if (target == null) { Patrol(); return; }

        if (TurnNumber - lastFlip > 30) { moveSign *= -1; lastFlip = TurnNumber; }

        double eDir = DirectionTo(target.X, target.Y);
        (double Dir, double Spd)[] candidates = IsAggressive()
            ? new[] { (eDir, 8.0), (eDir + 20, 7.0), (eDir - 20, 7.0) }
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

    // kalau ga ada target, jalan-jalan dulu sambil nunggu radar nangkep sesuatu
    void Patrol()
    {
        bool nearWall = X < WALL_MARGIN || Y < WALL_MARGIN
                     || X > ArenaWidth - WALL_MARGIN || Y > ArenaHeight - WALL_MARGIN;
        TurnRate = nearWall ? Math.Clamp(BearingTo(ArenaWidth / 2.0, ArenaHeight / 2.0), -10, 10) : 6;
        TargetSpeed = 6;
    }

    bool IsAggressive() => Energy < AGGRO_THRESHOLD;

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