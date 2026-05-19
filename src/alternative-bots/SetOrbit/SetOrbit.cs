using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class SetOrbit : Bot
{
    // ---------- Konstanta umum ----------
    const double WALL_MARGIN = 90;
    const double WALL_DANGER = 130;
    const double IDEAL_DISTANCE = 320;
    const double CLOSE_DISTANCE = 170;
    const double FAR_DISTANCE = 560;
    const double LOW_ENERGY = 22;
    const double RAM_KILL_ENERGY = 5.5;
    const double RAM_DISTANCE = 135;
    const int ENEMY_MEMORY_TURNS = 14;
    const int FIRE_MEMORY_TURNS = 4;

    // ---------- Data musuh ----------
    class EnemyInfo
    {
        public int Id;
        public double X;
        public double Y;
        public double Energy;
        public double Speed;
        public double Direction;
        public int LastSeen;
    }

    readonly Dictionary<int, EnemyInfo> enemies = new Dictionary<int, EnemyInfo>();
    int radarDirection = 1;
    int lastMoveFlipTurn = 0;
    int movementSign = 1;

    static void Main(string[] args)
    {
        new SetOrbit().Start();
    }

    SetOrbit() : base(BotInfo.FromFile("SetOrbit.json")) { }

    public override void Run()
    {
        BodyColor = Color.DarkSlateBlue;
        TurretColor = Color.MediumPurple;
        RadarColor = Color.LightSkyBlue;
        GunColor = Color.White;
        BulletColor = Color.Cyan;
        ScanColor = Color.LightBlue;
        TracksColor = Color.Black;

        AdjustGunForBodyTurn = true;
        AdjustRadarForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        SetFireAssist(false);

        MaxSpeed = 8;
        MaxTurnRate = 10;
        MaxGunTurnRate = 20;
        MaxRadarTurnRate = 45;

        while (IsRunning)
        {
            ExecuteGreedyTurn();
            Go();
        }
    }

    void ExecuteGreedyTurn()
    {
        RemoveOldEnemies();

        EnemyInfo target = SelectGreedyTarget();

        ControlRadar(target);
        ControlGunAndFire(target);
        ControlMovement(target);
    }


    EnemyInfo SelectGreedyTarget()
    {
        EnemyInfo best = null;
        double bestScore = double.NegativeInfinity;

        foreach (EnemyInfo enemy in enemies.Values)
        {
            int age = TurnNumber - enemy.LastSeen;
            if (age > ENEMY_MEMORY_TURNS)
                continue;
            if (enemy.Energy <= 0)
                continue;
            double distance = DistanceTo(enemy.X, enemy.Y);
            if (distance > 1200)
                continue;
            double freshnessScore = 1.0 - age / (double)ENEMY_MEMORY_TURNS;
            double stalePenalty = age * 0.22;
            double weakEnemyScore = Math.Max(0, 100 - enemy.Energy) / 100.0;
            double distanceScore = 1.0 - Math.Min(1.0, Math.Abs(distance - IDEAL_DISTANCE) / 500.0);

            double speedPenalty = Math.Min(1.0, Math.Abs(enemy.Speed) / 8.0);
            double hitChanceScore = 1.0 - speedPenalty * 0.45;

            double killBonusScore = 0.0;

            if (age <= FIRE_MEMORY_TURNS)
            {
                killBonusScore = enemy.Energy <= 16 ? 1.2 : 0.0;

                if (enemy.Energy <= 6)
                    killBonusScore += 1.0;
            }

            double closeDangerPenalty = 0.0;

            if (distance < CLOSE_DISTANCE && enemy.Energy > Energy)
                closeDangerPenalty = 0.8;

            double score =
                2.2 * hitChanceScore +
                1.8 * weakEnemyScore +
                1.5 * distanceScore +
                1.3 * freshnessScore +
                killBonusScore -
                closeDangerPenalty -
                stalePenalty;

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }

    void ControlRadar(EnemyInfo target)
    {
        if (target == null)
        {
            RadarTurnRate = 45 * radarDirection;
            return;
        }

        double radarBearing = RadarBearingTo(target.X, target.Y);

        // Overshoot agar radar tetap menyapu target walau target bergerak.
        RadarTurnRate = Clamp(radarBearing * 2.0, -45, 45);

        if (Math.Abs(radarBearing) < 2)
            radarDirection *= -1;
    }

    void ControlGunAndFire(EnemyInfo target)
    {
        if (target == null)
        {
            GunTurnRate = 0;
            SetFire(0);
            return;
        }

        int targetAge = TurnNumber - target.LastSeen;

        if (targetAge > FIRE_MEMORY_TURNS || target.Energy <= 0)
        {
            SetFire(0);

            RadarTurnRate = 45 * radarDirection;
            return;
        }

        double distance = DistanceTo(target.X, target.Y);
        double firePower = ChooseGreedyFirePower(target, distance);

        double bulletSpeed = CalcBulletSpeed(firePower);
        double travelTime = distance / bulletSpeed;

        double predictedX = target.X + Math.Sin(ToRadians(target.Direction)) * target.Speed * travelTime;
        double predictedY = target.Y + Math.Cos(ToRadians(target.Direction)) * target.Speed * travelTime;

        predictedX = Clamp(predictedX, 18, ArenaWidth - 18);
        predictedY = Clamp(predictedY, 18, ArenaHeight - 18);

        double gunBearing = GunBearingTo(predictedX, predictedY);
        GunTurnRate = Clamp(gunBearing, -20, 20);

        double aimTolerance = GetAimTolerance(distance);

        bool aimReady = Math.Abs(gunBearing) <= aimTolerance;
        bool safeEnergy = Energy > firePower + 0.5;
        bool reasonableDistance = distance <= 700;

        if (GunHeat == 0 && aimReady && safeEnergy && reasonableDistance)
        {
            SetFire(firePower);
        }
        else
        {
            SetFire(0);
        }
    }

    double ChooseGreedyFirePower(EnemyInfo target, double distance)
    {
        if (Energy < LOW_ENERGY)
        {
            if (distance < 220)
                return 1.2;

            return 0.8;
        }

        if (target.Energy <= 4)
            return 0.8;

        if (target.Energy <= 8)
            return 1.2;

        if (distance < 160)
            return 3.0;

        if (distance < 280)
            return 2.4;

        if (distance < 450)
            return 1.8;

        if (distance < 650)
            return 1.2;

        return 0.8;
    }

    double GetAimTolerance(double distance)
    {
        if (distance < 180)
            return 7.0;

        if (distance < 350)
            return 4.5;

        if (distance < 550)
            return 2.8;

        return 1.6;
    }

    void ControlMovement(EnemyInfo target)
    {
        if (target == null)
        {
            MoveWithoutTarget();
            return;
        }

        int targetAge = TurnNumber - target.LastSeen;

        if (targetAge > ENEMY_MEMORY_TURNS || target.Energy <= 0)
        {
            MoveWithoutTarget();
            return;
        }

        double distance = DistanceTo(target.X, target.Y);

        if (targetAge <= FIRE_MEMORY_TURNS &&
            target.Energy <= RAM_KILL_ENERGY &&
            distance <= RAM_DISTANCE &&
            Energy > 25)
        {
            double bearing = BearingTo(target.X, target.Y);
            TurnRate = Clamp(bearing, -10, 10);
            TargetSpeed = 8;
            return;
        }

        if (TurnNumber - lastMoveFlipTurn > 35)
        {
            movementSign *= -1;
            lastMoveFlipTurn = TurnNumber;
        }

        double enemyDirection = DirectionTo(target.X, target.Y);

        MovementCandidate[] candidates =
        {
            new MovementCandidate(enemyDirection + 90,  8),
            new MovementCandidate(enemyDirection - 90,  8),
            new MovementCandidate(enemyDirection + 120, 6),
            new MovementCandidate(enemyDirection - 120, 6),
            new MovementCandidate(enemyDirection + 180, 7),
            new MovementCandidate(enemyDirection,      5)
        };

        MovementCandidate best = candidates[0];
        double bestScore = double.NegativeInfinity;

        foreach (MovementCandidate candidate in candidates)
        {
            double score = EvaluateMovement(candidate, target);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        double desiredBearing = CalcBearing(NormalizeAbsoluteAngle(best.Direction));
        TurnRate = Clamp(desiredBearing, -10, 10);

        if (Math.Abs(desiredBearing) > 100)
        {
            TargetSpeed = -best.Speed * movementSign;
        }
        else
        {
            TargetSpeed = best.Speed * movementSign;
        }
    }

    double EvaluateMovement(MovementCandidate candidate, EnemyInfo target)
    {
        double radians = ToRadians(candidate.Direction);

        double futureX = X + Math.Sin(radians) * candidate.Speed * 14;
        double futureY = Y + Math.Cos(radians) * candidate.Speed * 14;

        double wallDistance = Min4(
            futureX,
            futureY,
            ArenaWidth - futureX,
            ArenaHeight - futureY
        );

        double wallScore;

        if (wallDistance < WALL_MARGIN)
            wallScore = -3.0;
        else
            wallScore = Math.Min(1.5, wallDistance / 220.0);

        double futureDistanceToEnemy = Distance(futureX, futureY, target.X, target.Y);

        double ideal = IDEAL_DISTANCE;

        if (Energy < LOW_ENERGY)
            ideal = 470;

        if (target.Energy <= 12 && Energy > 30)
            ideal = 230;

        double distanceScore =
            1.0 - Math.Min(1.0, Math.Abs(futureDistanceToEnemy - ideal) / 450.0);

        double directionToEnemy = DirectionTo(target.X, target.Y);
        double relative = NormalizeRelativeAngle(candidate.Direction - directionToEnemy);
        double lateralScore = Math.Abs(Math.Sin(ToRadians(relative)));

        double closePenalty = 0;

        if (futureDistanceToEnemy < CLOSE_DISTANCE && target.Energy > Energy)
            closePenalty = 1.5;

        double farPenalty = 0;

        if (futureDistanceToEnemy > FAR_DISTANCE && Energy > LOW_ENERGY)
            farPenalty = 0.8;

        return
            2.3 * wallScore +
            1.7 * distanceScore +
            1.4 * lateralScore -
            closePenalty -
            farPenalty;
    }

    void MoveWithoutTarget()
    {
        if (IsNearWall(WALL_DANGER))
        {
            double centerX = ArenaWidth / 2.0;
            double centerY = ArenaHeight / 2.0;

            TurnRate = Clamp(BearingTo(centerX, centerY), -10, 10);
            TargetSpeed = 7;
        }
        else
        {
            TurnRate = 6;
            TargetSpeed = 6;
        }
    }


    // ======================EVENT HANDLERS=====================


    public override void OnScannedBot(ScannedBotEvent e)
    {
        if (IsTeammate(e.ScannedBotId))
            return;

        enemies[e.ScannedBotId] = new EnemyInfo
        {
            Id = e.ScannedBotId,
            X = e.X,
            Y = e.Y,
            Energy = e.Energy,
            Speed = e.Speed,
            Direction = e.Direction,
            LastSeen = TurnNumber
        };
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        int deadBotId = GetDeadBotId(e);

        if (deadBotId != -1)
            enemies.Remove(deadBotId);

        SetFire(0);
        GunTurnRate = 0;
        RadarTurnRate = 45 * radarDirection;
    }

    public override void OnHitWall(HitWallEvent e)
    {
        movementSign *= -1;
        lastMoveFlipTurn = TurnNumber;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        movementSign *= -1;
        lastMoveFlipTurn = TurnNumber;
    }

    public override void OnHitBot(HitBotEvent e)
    {
        movementSign *= -1;
        TargetSpeed = -6;
    }

    public override void OnSkippedTurn(SkippedTurnEvent e)
    {
        RadarTurnRate = 45;
        GunTurnRate = 0;
        TurnRate = 0;
        TargetSpeed = 4;
        SetFire(0);
    }


    //===================UTILITIES===========================


    void RemoveOldEnemies()
    {
        List<int> removed = new List<int>();

        foreach (var pair in enemies)
        {
            EnemyInfo enemy = pair.Value;
            int age = TurnNumber - enemy.LastSeen;

            if (age > ENEMY_MEMORY_TURNS || enemy.Energy <= 0)
                removed.Add(pair.Key);
        }

        foreach (int id in removed)
            enemies.Remove(id);
    }

    int GetDeadBotId(BotDeathEvent e)
    {
        string[] possibleNames =
        {
            "VictimId",
            "BotId",
            "DeadBotId",
            "ScannedBotId"
        };

        foreach (string name in possibleNames)
        {
            var property = e.GetType().GetProperty(name);

            if (property == null)
                continue;

            object value = property.GetValue(e);

            if (value is int id)
                return id;
        }

        return -1;
    }

    bool IsNearWall(double margin)
    {
        return X < margin ||
               Y < margin ||
               X > ArenaWidth - margin ||
               Y > ArenaHeight - margin;
    }

    static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2;
        double dy = y1 - y2;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    static double Min4(double a, double b, double c, double d)
    {
        return Math.Min(Math.Min(a, b), Math.Min(c, d));
    }

    struct MovementCandidate
    {
        public double Direction;
        public double Speed;

        public MovementCandidate(double direction, double speed)
        {
            Direction = direction;
            Speed = speed;
        }
    }
}