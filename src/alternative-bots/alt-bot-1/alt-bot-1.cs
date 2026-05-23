using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class TemplateBot : Bot
{
    class EnemyInfo
    {
        public int BotId;
        public double X, Y, Energy, Direction, Speed;
        public long LastSeen;
    }

    readonly Dictionary<int, EnemyInfo> _enemies = new();
    EnemyInfo _target;

    static void Main(string[] args) => new TemplateBot().Start();
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

        AdjustRadarForBodyTurn = AdjustGunForBodyTurn = AdjustRadarForGunTurn = true;

        while (IsRunning)
        {
            CleanEnemies();
            _target = PickNearestTarget();

            if (_target == null)
            {
                // Cari musuh kalau belum ada target
                RadarTurnRate = 30;
                GunTurnRate = 0;
                TurnRate = 0;
                TargetSpeed = 4;
            }
            else
            {
                LockRadar(_target);
                LockGunAndFire(_target);
                ChaseAndRam(_target);
            }
            Go();
        }
    }

    public override void OnScannedBot(ScannedBotEvent evt)
    {
        // Simpan data musuh yang masih hidup
        if (evt.Energy <= 0) return;

        _enemies[evt.ScannedBotId] = new EnemyInfo
        {
            BotId = evt.ScannedBotId,
            X = evt.X,
            Y = evt.Y,
            Energy = evt.Energy,
            Direction = evt.Direction,
            Speed = evt.Speed,
            LastSeen = TurnNumber
        };
    }

    public override void OnHitByBullet(HitByBulletEvent e) { }
    public override void OnHitBot(HitBotEvent e) => TargetSpeed = 8; // Terus dorong/tabrak saat tabrakan terjadi
    public override void OnHitWall(HitWallEvent e) => TargetSpeed = -8; // Mundur sejenak jika menabrak dinding
    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);
        if (_target?.BotId == e.VictimId) _target = null;
    }

    // Bersihkan data musuh yang sudah lama tidak terlihat atau mati
    void CleanEnemies()
    {
        foreach (var id in _enemies.Keys.ToList())
        {
            if (TurnNumber - _enemies[id].LastSeen > 10 || _enemies[id].Energy <= 0)
                _enemies.Remove(id);
        }
    }

    // Pilih target terdekat (Greedy)
    EnemyInfo PickNearestTarget()
    {
        EnemyInfo nearest = null;
        double minDist = double.MaxValue;
        foreach (var e in _enemies.Values)
        {
            double dist = DistanceTo(e.X, e.Y);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = e;
            }
        }
        return nearest;
    }

    // Arahkan radar ke target
    void LockRadar(EnemyInfo target)
    {
        double radarBearing = RadarBearingTo(target.X, target.Y);
        RadarTurnRate = Math.Clamp(radarBearing * 2.0, -MaxRadarTurnRate, MaxRadarTurnRate);
    }

    // Arahkan gun lalu tembak jika sudah sejajar dengan target
    void LockGunAndFire(EnemyInfo target)
    {
        double gunBearing = GunBearingTo(target.X, target.Y);
        GunTurnRate = Math.Clamp(gunBearing, -MaxGunTurnRate, MaxGunTurnRate);

        double dist = DistanceTo(target.X, target.Y);

        // Tembak hanya kalau target dekat dan gun sudah cukup sejajar, dengan firepower maksimum untuk hit chance maksimal
        if (dist <= 120 && TurnNumber - target.LastSeen <= 10 && GunHeat == 0 && Math.Abs(gunBearing) <= 15 && Energy > 0.5)
        {
            SetFire(Math.Min(3.0, Energy - 0.1));
        }
    }

    // Body menghadap target lalu maju dengan kecepatan maksimal untuk ramming
    void ChaseAndRam(EnemyInfo target)
    {
        double bearing = BearingTo(target.X, target.Y);
        TurnRate = Math.Clamp(bearing, -MaxTurnRate, MaxTurnRate);
        TargetSpeed = 8; // Kecepatan maksimum
    }
}
