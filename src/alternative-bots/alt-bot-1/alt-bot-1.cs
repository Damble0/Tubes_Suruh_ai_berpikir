using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class TemplateBot : Bot
{
    // ── Konstanta ───────────────────────────────────────────
    const double LOW_ENERGY_THRESHOLD = 20.0;
    const double WALL_MARGIN = 60.0;
    const double WALL_STICK = 140.0;
    const double CLOSE_RANGE = 150.0;
    const double STRAFE_DISTANCE = 80.0;

    // ── State musuh ─────────────────────────────────────────
    class EnemyInfo
    {
        public int BotId;
        public double X, Y;
        public double Energy;
        public double Direction;
        public double PrevDirection;
        public double Speed;
        public long LastSeen;
    }

    readonly Dictionary<int, EnemyInfo> _enemies = new();
    EnemyInfo _target;
    int _strafeDir = 1;

    static void Main(string[] args)
    {
        new TemplateBot().Start();
    }

    TemplateBot() : base(BotInfo.FromFile("alt-bot-1.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(0xFF, 0x8C, 0x00);
        TurretColor = Color.FromArgb(0xFF, 0xA5, 0x00);
        RadarColor = Color.FromArgb(0xFF, 0xD7, 0x00);
        BulletColor = Color.FromArgb(0xFF, 0x45, 0x00);
        ScanColor = Color.FromArgb(0xFF, 0xFF, 0x00);
        TracksColor = Color.FromArgb(0x99, 0x33, 0x00);
        GunColor = Color.FromArgb(0xCC, 0x55, 0x00);

        AdjustRadarForBodyTurn = true;
        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;


        while (IsRunning)
        {

            TurnRadarRight(360);
        }
    }

    public override void OnScannedBot(ScannedBotEvent evt)
    {

        double prevDir = _enemies.ContainsKey(evt.ScannedBotId)
            ? _enemies[evt.ScannedBotId].Direction
            : evt.Direction;

        // Update info musuh yang ter-scan
        _enemies[evt.ScannedBotId] = new EnemyInfo
        {
            BotId = evt.ScannedBotId,
            X = evt.X,
            Y = evt.Y,
            Energy = evt.Energy,
            Direction = evt.Direction,
            PrevDirection = prevDir,
            Speed = evt.Speed,
            LastSeen = TurnNumber
        };

        // ── GREEDY LAYER 1: Survival Check ─────────────────
        if (Energy < LOW_ENERGY_THRESHOLD)
        {
            EvadeAndSurvive(evt);
            return;
        }

        // ── GREEDY LAYER 2: Target Selection ───────────────
        SelectBestTarget();
        if (_target == null) return;

        EnemyInfo aimData = (evt.ScannedBotId == _target.BotId)
            ? _enemies[evt.ScannedBotId]
            : _target;

        double distToTarget = DistanceTo(aimData.X, aimData.Y);

        // ── GREEDY LAYER 3: Firepower ──────────────────────
        double firePower = CalculateFirePower(distToTarget, aimData.Energy);
        AimAndFire(aimData, firePower);

        // ── GREEDY LAYER 4: Movement ───────────────────────
        StrafePerpendicularToTarget(_target);
    }

    public override void OnHitByBullet(HitByBulletEvent evt)
    {

        var bearing = CalcBearing(evt.Bullet.Direction);

        TurnLeft(90 - bearing);
        _strafeDir *= -1;
    }

    // ── Event tambahan ──────────────────────────────────────

    public override void OnHitBot(HitBotEvent evt)
    {

        _strafeDir *= -1;
        Back(50);
    }

    public override void OnHitWall(HitWallEvent evt)
    {
        _strafeDir *= -1;
        Back(40);
        double toCenterAngle = NormalizeRelativeAngle(
            DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0) - Direction
        );
        TurnRight(toCenterAngle * 0.5);
    }

    public override void OnBotDeath(BotDeathEvent evt)
    {
        _enemies.Remove(evt.VictimId);
        if (_target != null && _target.BotId == evt.VictimId)
            _target = null;
    }

    void EvadeAndSurvive(ScannedBotEvent evt)
    {
        double escapeHeading = DirectionTo(evt.X, evt.Y) + 180;

        escapeHeading = WallSmoothing(escapeHeading);

        double escapeAngle = NormalizeRelativeAngle(escapeHeading - Direction);
        TurnRight(escapeAngle);
        Forward(120);

        if (GunHeat == 0 && Energy > 5)
            Fire(0.5);
    }

    void SelectBestTarget()
    {
        EnemyInfo best = null;
        double bestScore = -1;
        double currentTargetScore = -1;

        foreach (var kv in _enemies)
        {
            var enemy = kv.Value;

            if (TurnNumber - enemy.LastSeen > 10) continue;

            double dist = DistanceTo(enemy.X, enemy.Y);
            if (dist < 1) dist = 1;

            double score = (100.0 / Math.Max(enemy.Energy, 1.0)) / dist;

            double angleToEnemy = DirectionTo(enemy.X, enemy.Y);
            double relativeHeading = NormalizeRelativeAngle(enemy.Direction - angleToEnemy);
            double lateralSpeed = Math.Abs(enemy.Speed * Math.Sin(ToRadians(relativeHeading)));
            double hitDifficulty = 1.0 / (1.0 + lateralSpeed * 0.05);
            score *= hitDifficulty;

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }

            if (_target != null && enemy.BotId == _target.BotId)
                currentTargetScore = score;
        }

        if (_target != null && currentTargetScore > 0 && best != null
            && TurnNumber - _target.LastSeen <= 8)
        {
            if (bestScore < currentTargetScore * 1.25)
                return;
        }

        _target = best;
    }


    double CalculateFirePower(double distance, double enemyEnergy)
    {
        double power;
        if (distance < 100)
            power = 3.0;
        else if (distance < CLOSE_RANGE)
            power = 2.0;
        else if (distance < 350)
            power = 1.5;
        else
            power = 1.0;


        if (enemyEnergy > 0 && enemyEnergy < power * 4)
            power = Math.Max(0.1, enemyEnergy / 4.0);

        power = Math.Min(power, Energy * 0.2);
        power = Math.Max(0.1, power);

        return power;
    }

    void AimAndFire(EnemyInfo target, double firePower)
    {
        double bulletSpeed = 20 - 3 * firePower;
        double distance = DistanceTo(target.X, target.Y);

        if (distance > 500)
            return;

        double travelTime = distance / bulletSpeed;

        double predX = target.X + Math.Sin(ToRadians(target.Direction)) * target.Speed * travelTime;
        double predY = target.Y + Math.Cos(ToRadians(target.Direction)) * target.Speed * travelTime;

        predX = Math.Max(18, Math.Min(predX, ArenaWidth - 18));
        predY = Math.Max(18, Math.Min(predY, ArenaHeight - 18));

        double gunTurn = NormalizeRelativeAngle(DirectionTo(predX, predY) - GunDirection);


        if (Math.Abs(gunTurn) > 30)
            return;


        TurnGunRight(gunTurn);


        if (GunHeat == 0)
            Fire(firePower);
    }

    void StrafePerpendicularToTarget(EnemyInfo target)
    {

        double desiredHeading = DirectionTo(target.X, target.Y) + 90 * _strafeDir;


        double smoothedHeading = WallSmoothing(desiredHeading);


        double diff = Math.Abs(NormalizeRelativeAngle(smoothedHeading - desiredHeading));
        if (diff > 90)
        {
            _strafeDir *= -1;
            desiredHeading = DirectionTo(target.X, target.Y) + 90 * _strafeDir;
            smoothedHeading = WallSmoothing(desiredHeading);
        }

        double turnAngle = NormalizeRelativeAngle(smoothedHeading - Direction);
        TurnRight(turnAngle);
        Forward(STRAFE_DISTANCE);
        TurnRadarRight(360);
    }

    double WallSmoothing(double desiredHeading)
    {
        double heading = desiredHeading;
        double step = 5.0 * _strafeDir;

        for (int i = 0; i < 36; i++)
        {
            double projX = X + Math.Sin(ToRadians(heading)) * WALL_STICK;
            double projY = Y + Math.Cos(ToRadians(heading)) * WALL_STICK;

            if (projX > WALL_MARGIN && projX < ArenaWidth - WALL_MARGIN &&
                projY > WALL_MARGIN && projY < ArenaHeight - WALL_MARGIN)
            {
                return heading;
            }

            heading += step;
        }


        return DirectionTo(ArenaWidth / 2.0, ArenaHeight / 2.0);
    }

    bool IsNearWall() =>
        X < WALL_MARGIN || Y < WALL_MARGIN ||
        X > ArenaWidth - WALL_MARGIN || Y > ArenaHeight - WALL_MARGIN;

    static double ToRadians(double deg) => deg * Math.PI / 180.0;
}
