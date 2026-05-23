using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class SetOrbit : Bot
{
    const double IDEAL_DISTANCE = 320, LOW_ENERGY = 22;
    const int ENEMY_MEMORY_TURNS = 14, FIRE_MEMORY_TURNS = 4;

    class Enemy { public double X, Y, Energy, Speed, Direction; public int LastSeen; }
    readonly Dictionary<int, Enemy> enemies = new();
    int radarDirection = 1, lastMoveFlipTurn = 0, movementSign = 1;

    static void Main(string[] args) => new SetOrbit().Start();
    SetOrbit() : base(BotInfo.FromFile("SetOrbit.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(0x48, 0x3D, 0x8B); TurretColor = Color.FromArgb(0x93, 0x70, 0xDB);
        RadarColor = Color.FromArgb(0x87, 0xCE, 0xFA); GunColor = Color.White; BulletColor = Color.Cyan;
        AdjustGunForBodyTurn = AdjustRadarForBodyTurn = AdjustRadarForGunTurn = true;
        SetFireAssist(false);
        MaxSpeed = 8;
        MaxTurnRate = 10;
        MaxGunTurnRate = 20;
        MaxRadarTurnRate = 45;
        while (IsRunning) { RunStrategy(); Go(); }
    }

    void RunStrategy()
    {
        foreach (var id in enemies.Where(p => TurnNumber - p.Value.LastSeen > ENEMY_MEMORY_TURNS || p.Value.Energy <= 0).Select(p => p.Key).ToList())
            enemies.Remove(id);

        var target = PickTarget();
        ControlRadar(target);
        AimAndShoot(target);
        ControlMovement(target);
    }

    // Memilih target berdasarkan skor tertinggi
    Enemy PickTarget()
    {
        Enemy best = null; double bestScore = double.NegativeInfinity;
        foreach (var enemy in enemies.Values)
        {
            int age = TurnNumber - enemy.LastSeen;
            if (age > ENEMY_MEMORY_TURNS || enemy.Energy <= 0) continue;

            double distance = DistanceTo(enemy.X, enemy.Y);
            if (distance > 1200) continue;

            double score = 2.2 * (1.0 - Math.Min(1.0, Math.Abs(enemy.Speed) / 8.0) * 0.45)
                         + 1.8 * (Math.Max(0, 100 - enemy.Energy) / 100.0)
                         + 1.5 * (1.0 - Math.Min(1.0, Math.Abs(distance - IDEAL_DISTANCE) / 500.0))
                         + 1.3 * (1.0 - age / (double)ENEMY_MEMORY_TURNS)
                         + (age <= FIRE_MEMORY_TURNS ? (enemy.Energy <= 16 ? 1.2 : 0) + (enemy.Energy <= 6 ? 1.0 : 0) : 0)
                         - (distance < 170 && enemy.Energy > Energy ? 0.8 : 0.0) - age * 0.22;

            if (score > bestScore) { bestScore = score; best = enemy; }
        }
        return best;
    }

    void ControlRadar(Enemy target)
    {
        if (target == null) { RadarTurnRate = 45 * radarDirection; return; }
        double radarBearing = RadarBearingTo(target.X, target.Y);
        RadarTurnRate = Math.Clamp(radarBearing * 2.0, -45, 45);
        if (Math.Abs(radarBearing) < 2) radarDirection *= -1;
    }

    void AimAndShoot(Enemy target)
    {
        if (target == null || TurnNumber - target.LastSeen > FIRE_MEMORY_TURNS || target.Energy <= 0)
        {
            GunTurnRate = target == null ? 0 : GunTurnRate;
            if (target != null) RadarTurnRate = 45 * radarDirection;
            return;
        }

        double distance = DistanceTo(target.X, target.Y);
        double firePower = GetFirePower(distance, target.Energy);

        // Perkirakan posisi musuh sebelum menembak
        double travelTime = distance / CalcBulletSpeed(firePower);
        double targetX = Math.Clamp(target.X + Math.Sin(ToRadians(target.Direction)) * target.Speed * travelTime, 18, ArenaWidth - 18);
        double targetY = Math.Clamp(target.Y + Math.Cos(ToRadians(target.Direction)) * target.Speed * travelTime, 18, ArenaHeight - 18);

        double gunBearing = GunBearingTo(targetX, targetY);
        GunTurnRate = Math.Clamp(gunBearing, -20, 20);

        double aimTolerance = distance < 200 ? 6.0 : (distance < 450 ? 3.5 : 1.5);
        if (GunHeat == 0 && Math.Abs(gunBearing) <= aimTolerance && Energy > firePower + 0.5 && distance <= 700)
        {
            SetFire(firePower);
        }
    }

    // Mengatur power peluru sesuai jarak dan energi
    double GetFirePower(double distance, double targetEnergy)
    {
        if (Energy < LOW_ENERGY) return distance < 220 ? 1.2 : 0.8;
        if (targetEnergy <= 4) return 0.8;
        if (targetEnergy <= 8) return 1.2;

        return distance switch
        {
            < 160 => 3.0,
            < 280 => 2.4,
            < 450 => 1.8,
            < 650 => 1.2,
            _ => 0.8
        };
    }

    // Pilih arah gerak yang aman sambil menjaga jarak
    void ControlMovement(Enemy target)
    {
        if (target == null) { Patrol(); return; }
        double distance = DistanceTo(target.X, target.Y);

        // Tabrak musuh kalau energinya sudah rendah
        if (TurnNumber - target.LastSeen <= FIRE_MEMORY_TURNS && target.Energy <= 5.5 && distance <= 135 && Energy > 25)
        {
            TurnRate = Math.Clamp(BearingTo(target.X, target.Y), -10, 10);
            TargetSpeed = 8; return;
        }

        if (TurnNumber - lastMoveFlipTurn > 35) { movementSign *= -1; lastMoveFlipTurn = TurnNumber; }

        double enemyDir = DirectionTo(target.X, target.Y);
        var candidates = new (double Dir, double Speed)[] { (enemyDir + 90, 8), (enemyDir - 90, 8), (enemyDir + 120, 6), (enemyDir - 120, 6), (enemyDir + 180, 7), (enemyDir, 5) };
        var best = candidates[0]; double bestScore = double.NegativeInfinity;

        foreach (var c in candidates)
        {
            double rad = ToRadians(c.Dir), fx = X + Math.Sin(rad) * c.Speed * 14, fy = Y + Math.Cos(rad) * c.Speed * 14;
            double wallDist = Math.Min(Math.Min(fx, fy), Math.Min(ArenaWidth - fx, ArenaHeight - fy));
            double wallScore = wallDist < 90 ? -3.0 : Math.Min(1.5, wallDist / 220.0);

            double fDist = Math.Sqrt(Math.Pow(fx - target.X, 2) + Math.Pow(fy - target.Y, 2));
            double ideal = Energy < LOW_ENERGY ? 470 : (target.Energy <= 12 && Energy > 30 ? 230 : IDEAL_DISTANCE);
            double score = 2.3 * wallScore + 1.7 * (1.0 - Math.Min(1.0, Math.Abs(fDist - ideal) / 450.0)) + 1.4 * Math.Abs(Math.Sin(ToRadians(NormalizeRelativeAngle(c.Dir - DirectionTo(target.X, target.Y))))) - (fDist < 170 && target.Energy > Energy ? 1.5 : 0) - (fDist > 560 && Energy > LOW_ENERGY ? 0.8 : 0);

            if (score > bestScore) { bestScore = score; best = c; }
        }

        double bearing = CalcBearing(NormalizeAbsoluteAngle(best.Dir));
        TurnRate = Math.Clamp(bearing, -10, 10);
        TargetSpeed = (Math.Abs(bearing) > 100 ? -best.Speed : best.Speed) * movementSign;
    }

    void Patrol() { TurnRate = 6; TargetSpeed = 6; }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        if (!IsTeammate(e.ScannedBotId))
            enemies[e.ScannedBotId] = new Enemy { X = e.X, Y = e.Y, Energy = e.Energy, Speed = e.Speed, Direction = e.Direction, LastSeen = TurnNumber };
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        enemies.Remove(e.VictimId);
        GunTurnRate = 0; RadarTurnRate = 45 * radarDirection;
    }

    public override void OnHitWall(HitWallEvent e) { movementSign *= -1; lastMoveFlipTurn = TurnNumber; }
    public override void OnHitByBullet(HitByBulletEvent e) { movementSign *= -1; lastMoveFlipTurn = TurnNumber; }
    public override void OnHitBot(HitBotEvent e) { movementSign *= -1; TargetSpeed = -6; }
    public override void OnSkippedTurn(SkippedTurnEvent e) { RadarTurnRate = 45; GunTurnRate = TurnRate = 0; TargetSpeed = 4; }

    static double ToRadians(double deg) => deg * Math.PI / 180.0;
}