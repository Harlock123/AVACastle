using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVACastle.Models;

namespace AVACastle.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract
}

public class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    // The game runs on a fixed 4:3 playfield; GameCanvas scales it to fit the window.
    public const double Width = 1024;
    public const double Height = 768;
    public static readonly Point Center = new(Width / 2, Height / 2);

    public const int StartingLives = 3;
    public const int MaxLives = 9;
    public const int CannonScoreValue = 2000;
    private const int MaxMines = 3;
    private const double RespawnDelay = 2.0;
    private const double CastleRebuildDelay = 3.0;
    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 60.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns

    // Colours of the original cabinet's screen overlay, by ring slot (outer, middle, inner).
    public static readonly Color[] SlotColors =
    {
        Color.FromRgb(255, 225, 74),
        Color.FromRgb(255, 150, 40),
        Color.FromRgb(255, 60, 60)
    };

    public Ship Ship { get; private set; }
    public bool ShipAlive { get; private set; }
    public Castle? Castle { get; private set; }
    public List<Bullet> Bullets { get; } = new();
    public List<Mine> Mines { get; } = new();
    public List<Fireball> Fireballs { get; } = new();
    public List<Explosion> Explosions { get; } = new();
    public List<(Point Position, double Brightness)> Stars { get; } = new();

    public int Score { get; private set; }
    public int HighScore { get; private set; }
    public int Lives { get; private set; }
    public int Level { get; private set; }
    public GameState State { get; private set; }

    /// <summary>Seconds left before the next castle appears, while the old one is a smoking crater.</summary>
    public double CastleRebuildTimer { get; private set; }

    public bool IsThrusting => ShipAlive && (State is GameState.Playing or GameState.Attract) && _controls.Thrust;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private bool _wasShootingLastFrame;
    private bool _wasThrustingLastFrame;
    private double _respawnTimer;
    private double _mineTimer;
    private double _bounceSoundTimer;
    private double _gameOverTimer;
    private double _demoAngle;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private ShipControls _controls;
    private readonly AttractPilot _pilot = new();

    private static readonly string HighScorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVACastle", "highscore.txt");

    public GameEngine()
    {
        Ship = new Ship(SpawnPoints[0], 0);
        State = GameState.StartScreen;
        Castle = new Castle(Center, 1, growFromCore: false);
        HighScore = LoadHighScore();

        for (int i = 0; i < 110; i++)
        {
            var star = new Point(Random.NextDouble() * Width, Random.NextDouble() * Height);
            if (Distance(star, Center) > Castle.SlotRadius[0] + 15)
                Stars.Add((star, 0.25 + Random.NextDouble() * 0.6));
        }
    }

    public void Dispose()
    {
        Sound.Dispose();
        Input.Dispose();
    }

    // Where the ship (re)appears: the middle of each edge, facing the castle.
    private static readonly Point[] SpawnPoints =
    {
        new(110, Height / 2),
        new(Width - 110, Height / 2),
        new(Width / 2, 90),
        new(Width / 2, Height - 90)
    };

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.PlayRegenerate();
    }

    /// <summary>
    /// The game plays itself, silently, until it runs out of ships or time, then goes back to the
    /// title. Fire starts a real game; anything else returns to the title.
    /// </summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _pilot.Reset(Ship.Position);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopThruster();
        Sound.StopDrone();
        Sound.IsSilenced = false;
        ShipAlive = false;
        Bullets.Clear();
        Mines.Clear();
        Fireballs.Clear();
        Explosions.Clear();
        Castle = new Castle(Center, 1, growFromCore: false);
        _idleTimer = 0;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.IsSilenced = state == GameState.Attract;
        Sound.StopThruster();
        Score = 0;
        Lives = StartingLives;
        Level = 1;

        Bullets.Clear();
        Mines.Clear();
        Fireballs.Clear();
        Explosions.Clear();

        Castle = new Castle(Center, Level);
        SpawnShip();
    }

    private void SpawnShip()
    {
        // The edge point furthest from any mine or fireball still in flight.
        var threats = Mines.Select(m => m.Position).Concat(Fireballs.Select(f => f.Position)).ToList();
        var spot = SpawnPoints
            .OrderByDescending(p => threats.Count == 0 ? Random.NextDouble() : threats.Min(t => Distance(p, t)))
            .First();

        Ship.Respawn(spot, Math.Atan2(Center.Y - spot.Y, Center.X - spot.X));
        ShipAlive = true;
        _mineTimer = 2.5;
    }

    public void Update(double deltaTime)
    {
        bool shootPressed = Input.IsShooting && !_wasShootingLastFrame;
        _wasShootingLastFrame = Input.IsShooting;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                return;

            case GameState.StartScreen:
                UpdateIdleCastle(deltaTime);
                if (shootPressed)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= AttractIdleDelay)
                    StartAttract();
                return;

            case GameState.GameOver:
                UpdateIdleCastle(deltaTime);
                UpdateExplosions(deltaTime);
                _gameOverTimer -= deltaTime;
                if (shootPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (shootPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                _controls = _pilot.Fly(this, deltaTime);
                break;

            default:
                _controls = new ShipControls(Input.IsRotatingLeft, Input.IsRotatingRight, Input.IsThrusting, Input.IsShooting);
                break;
        }

        // The pilot's fire button is edge-triggered just like the player's.
        bool firePressed = State == GameState.Attract ? _controls.Fire : shootPressed;

        UpdateShip(deltaTime, firePressed);
        UpdateBullets(deltaTime);
        UpdateCastle(deltaTime);
        UpdateDrone();
        UpdateMines(deltaTime);
        UpdateFireballs(deltaTime);
        UpdateExplosions(deltaTime);
        CheckCollisions(deltaTime);
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopThruster();
            Sound.StopDrone();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
        {
            State = GameState.Playing;
        }
    }

    // On the title and game-over screens the castle keeps turning and the cannon sweeps around.
    private void UpdateIdleCastle(double deltaTime)
    {
        if (Castle == null)
            Castle = new Castle(Center, 1);

        _demoAngle += deltaTime * 0.6;
        Castle.Update(deltaTime);
        Castle.UpdateCannon(deltaTime,
            new Point(Center.X + Math.Cos(_demoAngle) * 300, Center.Y + Math.Sin(_demoAngle) * 300), canFire: false);
    }

    private void UpdateShip(double deltaTime, bool shootPressed)
    {
        if (!ShipAlive)
        {
            _respawnTimer -= deltaTime;
            if (_respawnTimer <= 0 && Lives > 0)
                SpawnShip();
            return;
        }

        if (_controls.RotateLeft)
            Ship.RotateLeft(deltaTime);
        if (_controls.RotateRight)
            Ship.RotateRight(deltaTime);

        // Apply mouse wheel rotation directly (faster than key rotation)
        if (State == GameState.Playing && Input.MouseWheelDelta != 0)
            Ship.Rotation += Input.MouseWheelDelta * 0.3;

        if (_controls.Thrust)
        {
            Ship.Thrust(deltaTime);
            Sound.PlayThruster();
        }
        else if (_wasThrustingLastFrame)
        {
            Sound.StopThruster();
        }
        _wasThrustingLastFrame = _controls.Thrust;

        if (shootPressed)
        {
            var bullet = Ship.Shoot(Bullets.Count);
            if (bullet != null)
            {
                Bullets.Add(bullet);
                Sound.PlayFire();
            }
        }

        Ship.Update(deltaTime, Width, Height);
    }

    private void UpdateBullets(double deltaTime)
    {
        foreach (var bullet in Bullets)
            bullet.Update(deltaTime, Width, Height);
    }

    private void UpdateCastle(double deltaTime)
    {
        if (Castle == null)
        {
            CastleRebuildTimer -= deltaTime;
            if (CastleRebuildTimer <= 0)
            {
                Level++;
                Castle = new Castle(Center, Level);
                Sound.PlayRegenerate();
            }
            return;
        }

        Castle.Update(deltaTime);

        var fireball = Castle.UpdateCannon(deltaTime, Ship.Position, canFire: ShipAlive);
        if (fireball != null)
        {
            Fireballs.Add(fireball);
            Sound.PlayFireball();
            _pilot.OnCannonFired();
        }
    }

    // The castle hums while it stands, a step higher every two castles; it falls silent when the
    // castle is destroyed, and comes back with the next one.
    private void UpdateDrone()
    {
        if (State == GameState.Playing && Castle != null)
            Sound.PlayDrone(Math.Clamp((Level + 1) / 2, 1, SoundManager.DroneStages));
        else
            Sound.StopDrone();
    }

    private void UpdateMines(double deltaTime)
    {
        _mineTimer -= deltaTime;
        if (_mineTimer <= 0 && Castle is { IsBuilding: false } && ShipAlive && Mines.Count < MaxMines)
        {
            // Mines leave the core in any direction except straight at the ship, then curve round
            // after it, so they tend to arrive from the side or behind.
            double toShip = Math.Atan2(Ship.Position.Y - Center.Y, Ship.Position.X - Center.X);
            Mines.Add(new Mine(Center, toShip + Math.PI / 2 + Random.NextDouble() * Math.PI, Level));
            Sound.PlayMine();
            _mineTimer = Math.Max(1.5, 4.0 - 0.25 * (Level - 1));
        }

        // With the ship gone, mines drift on toward where it was.
        foreach (var mine in Mines)
            mine.Update(deltaTime, Width, Height, Ship.Position);

        Mines.RemoveAll(m => !m.IsAlive);
    }

    private void UpdateFireballs(double deltaTime)
    {
        foreach (var fireball in Fireballs)
            fireball.Update(deltaTime, Width, Height);

        Fireballs.RemoveAll(f => !f.IsAlive);
    }

    private void UpdateExplosions(double deltaTime)
    {
        foreach (var explosion in Explosions)
            explosion.Update(deltaTime);

        Explosions.RemoveAll(e => e.IsFinished);
    }

    private void CheckCollisions(double deltaTime)
    {
        foreach (var bullet in Bullets)
        {
            if (!bullet.IsAlive) continue;

            if (Castle != null && BulletHitsCastle(bullet))
            {
                bullet.IsAlive = false;
                continue;
            }

            foreach (var mine in Mines)
            {
                if (mine.IsAlive && bullet.CollidesWith(mine))
                {
                    bullet.IsAlive = false;
                    mine.IsAlive = false;
                    Explosions.Add(Explosion.CreateMine(mine.Position));
                    Sound.PlayBreak();
                    break;
                }
            }
        }
        Bullets.RemoveAll(b => !b.IsAlive);
        Mines.RemoveAll(m => !m.IsAlive);

        if (!ShipAlive) return;

        _bounceSoundTimer -= deltaTime;
        if (Castle != null)
            BounceShipOffRings();

        if (Ship.IsInvincible) return;

        bool hit = Mines.Any(m => Ship.CollidesWith(m)) || Fireballs.Any(f => Ship.CollidesWith(f));
        if (!hit && Castle != null)
            hit = Distance(Ship.Position, Center) < Castle.CannonRadius + Ship.Radius - 4;

        if (hit)
            DestroyShip();
    }

    /// <summary>
    /// Works out what a bullet hit on its way through the castle this frame: the first standing ring
    /// segment it crossed, or the cannon if it found a way through every ring.
    /// </summary>
    private bool BulletHitsCastle(Bullet bullet)
    {
        var castle = Castle!;
        double prevD = Distance(bullet.PreviousPosition, Center);
        double curD = Distance(bullet.Position, Center);

        // Rings are checked in the order the bullet reaches them: outermost first when flying in.
        var order = Enumerable.Range(0, castle.Rings.Count);
        if (curD > prevD) order = order.Reverse();

        foreach (int slot in order)
        {
            var ring = castle.Rings[slot];
            double angle = AngleOf(bullet.Position);
            double r = ring.RadiusAt(angle);

            if ((prevD - r) * (curD - r) > 0 && Math.Abs(curD - r) > 3)
                continue;

            // Where along this frame's path the bullet met the ring.
            double t = Math.Abs(prevD - curD) < 1e-6 ? 1 : Math.Clamp((prevD - r) / (prevD - curD), 0, 1);
            var crossing = new Point(
                bullet.PreviousPosition.X + (bullet.Position.X - bullet.PreviousPosition.X) * t,
                bullet.PreviousPosition.Y + (bullet.Position.Y - bullet.PreviousPosition.Y) * t);

            int index = ring.SegmentAt(AngleOf(crossing));
            if (ring.Health[index] <= 0)
                continue;

            ring.Health[index]--;
            if (ring.Health[index] > 0)
            {
                Sound.PlayHit();
                return true;
            }

            Score += Castle.SlotPoints[slot];
            var (a, b) = ring.GetSegment(Center, index);
            Explosions.Add(Explosion.CreateSegment(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), SlotColors[slot]));
            Sound.PlayBreak();

            if (castle.Rings[0].IsEmpty)
            {
                castle.ShiftRings();
                Sound.PlayRegenerate();
            }
            return true;
        }

        if (curD < Castle.CannonRadius || DistanceToSegment(Center, bullet.PreviousPosition, bullet.Position) < Castle.CannonRadius)
        {
            DestroyCastle();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Standing ring segments are solid to the ship: it bounces off them. A ring growing out of the
    /// core pushes the ship outward with it rather than trapping it inside.
    /// </summary>
    private void BounceShipOffRings()
    {
        var castle = Castle!;
        foreach (var ring in castle.Rings)
        {
            double d = Distance(Ship.Position, Center);
            if (d < 1e-6) continue;

            double angle = AngleOf(Ship.Position);
            double r = ring.RadiusAt(angle);
            if (Math.Abs(d - r) >= Ship.Radius || !ring.IsIntactAt(angle))
                continue;

            var normal = new Vector((Ship.Position.X - Center.X) / d, (Ship.Position.Y - Center.Y) / d);
            bool expanding = ring.IsGrowing && ring.Radius < ring.TargetRadius;
            double side = d >= r || expanding ? 1 : -1;

            Ship.Position = new Point(
                Center.X + normal.X * (r + side * Ship.Radius),
                Center.Y + normal.Y * (r + side * Ship.Radius));

            double vn = Ship.Velocity.X * normal.X + Ship.Velocity.Y * normal.Y;
            if (vn * side < 0)
            {
                Ship.Velocity = new Vector(
                    (Ship.Velocity.X - 2 * vn * normal.X) * 0.75,
                    (Ship.Velocity.Y - 2 * vn * normal.Y) * 0.75);

                if (_bounceSoundTimer <= 0)
                {
                    Sound.PlayHit();
                    _bounceSoundTimer = 0.2;
                }
            }
        }
    }

    private void DestroyShip()
    {
        Explosions.Add(Explosion.CreateShip(Ship.Position));
        Sound.PlayExplosion();
        Sound.StopThruster();
        _wasThrustingLastFrame = false;

        ShipAlive = false;
        Bullets.Clear();
        Fireballs.Clear();
        foreach (var mine in Mines)
            Explosions.Add(Explosion.CreateMine(mine.Position));
        Mines.Clear();

        Lives--;
        if (Lives > 0)
        {
            _respawnTimer = RespawnDelay;
            return;
        }

        // A demo that runs out of ships lingers on the explosion, then hands back to the title.
        // It never ends in Game Over and never touches the high score.
        if (State == GameState.Attract)
        {
            _attractTimer = Math.Min(_attractTimer, 2.5);
            return;
        }

        State = GameState.GameOver;
        Sound.StopDrone();
        _gameOverTimer = GameOverInputDelay;
        if (Score > HighScore)
        {
            HighScore = Score;
            SaveHighScore(HighScore);
        }
    }

    private void DestroyCastle()
    {
        var castle = Castle!;
        Explosions.Add(Explosion.CreateCastle(Center, SlotColors[2]));
        Explosions.Add(Explosion.CreateCastle(Center, SlotColors[0]));
        for (int slot = 0; slot < castle.Rings.Count; slot++)
        {
            var ring = castle.Rings[slot];
            for (int i = 0; i < Ring.SegmentCount; i++)
            {
                if (ring.Health[i] <= 0) continue;
                var (a, b) = ring.GetSegment(Center, i);
                Explosions.Add(Explosion.CreateSegment(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), SlotColors[slot]));
            }
        }

        Sound.PlayCastleBoom();
        Score += CannonScoreValue;
        if (Lives < MaxLives)
        {
            Lives++;
            Sound.PlayExtraLife();
        }

        foreach (var mine in Mines)
            Explosions.Add(Explosion.CreateMine(mine.Position));
        Mines.Clear();
        Fireballs.Clear();

        Castle = null;
        CastleRebuildTimer = CastleRebuildDelay;
        _mineTimer = CastleRebuildDelay + 2.5;
    }

    private static double AngleOf(Point p) => Math.Atan2(p.Y - Center.Y, p.X - Center.X);

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + dx * t, a.Y + dy * t));
    }

    private static int LoadHighScore()
    {
        try
        {
            return File.Exists(HighScorePath) && int.TryParse(File.ReadAllText(HighScorePath).Trim(), out int score)
                ? score
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void SaveHighScore(int score)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HighScorePath)!);
            File.WriteAllText(HighScorePath, score.ToString());
        }
        catch
        {
            // A read-only home folder just means the high score isn't remembered.
        }
    }
}
