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
    Attract,
    EnterInitials,
    HighScores
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
    private const double GameOverBeforeEntry = 2.0; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

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
    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;

    /// <summary>The initials being entered, during GameState.EnterInitials.</summary>
    public InitialsEntry? Entry { get; private set; }

    /// <summary>The place a just-entered score took in the table (0 = top), or -1.</summary>
    public int HighlightIndex { get; private set; } = -1;

    /// <summary>The game just ended with a score that makes the table: initials entry follows.</summary>
    public bool IsEntryPending => _entryPending;
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
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _demoAngle;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private ShipControls _controls;
    private readonly AttractPilot _pilot = new();

    public GameEngine()
    {
        Ship = new Ship(SpawnPoints[0], 0);
        State = GameState.StartScreen;
        Castle = new Castle(Center, 1, growFromCore: false);

        for (int i = 0; i < 110; i++)
        {
            var star = new Point(Random.NextDouble() * Width, Random.NextDouble() * Height);
            if (Distance(star, Center) > Castle.SlotRadius[0] + 15)
                Stars.Add((star, 0.25 + Random.NextDouble() * 0.6));
        }
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

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
        HighlightIndex = -1;
    }

    /// <summary>
    /// Shows the table: after a new entry (with it highlighted), or as part of the idle cycle on the
    /// way from the title screen to the demo.
    /// </summary>
    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, Level);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
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
                    ShowHighScores(leadsToDemo: true);
                return;

            case GameState.HighScores:
                UpdateIdleCastle(deltaTime);
                UpdateExplosions(deltaTime);
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
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateIdleCastle(deltaTime);
                UpdateExplosions(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateIdleCastle(deltaTime);
                UpdateExplosions(deltaTime);
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
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
        _idleTimer = 0;
        _entryPending = Table.Qualifies(Score);
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
}
