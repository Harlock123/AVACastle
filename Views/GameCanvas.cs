using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVACastle.Engine;
using AVACastle.Models;

namespace AVACastle.Views;

public class GameCanvas : Control
{
    private readonly GameEngine _engine;
    private readonly DispatcherTimer _gameTimer;
    private DateTime _lastUpdate;
    private bool _wasPausePressed;
    private bool _wasBackPressed;

    private static readonly Color ShipColor = Color.FromRgb(235, 245, 255);
    private static readonly Color MineColor = Color.FromRgb(160, 255, 190);
    private static readonly Color FireballColor = Color.FromRgb(255, 240, 170);
    private static readonly Color TextColor = Color.FromRgb(120, 200, 255);
    private static readonly IBrush BulletBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 120, 200, 255)), 1);

    private static readonly Typeface HudFont = new("Consolas", FontStyle.Normal, FontWeight.Bold);

    // Pens are cached by colour, opacity and width: a frame draws a few hundred lines.
    private readonly Dictionary<(Color, byte, double), IPen> _pens = new();

    public GameCanvas()
    {
        _engine = new GameEngine();
        _lastUpdate = DateTime.Now;

        _gameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _gameTimer.Tick += GameLoop;

        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _gameTimer.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _gameTimer.Stop();
        _engine.Dispose();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        // Handle Ctrl+S (or Cmd+S on macOS) for screenshot
        if (e.Key == Avalonia.Input.Key.S &&
            (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ||
             e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)))
        {
            TakeScreenshot();
            e.Handled = true;
            return;
        }

        // While entering initials every key belongs to the entry (M is a letter, not mute).
        if (_engine.State == GameState.EnterInitials)
        {
            _engine.InitialsKey(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.M)
        {
            _engine.Sound.IsMuted = !_engine.Sound.IsMuted;
            if (_engine.Sound.IsMuted)
            {
                _engine.Sound.StopThruster();
                _engine.Sound.StopDrone();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Escape)
        {
            if (_engine.State == GameState.Playing)
            {
                _engine.Pause();
            }
            else if (_engine.State is GameState.Attract or GameState.HighScores)
            {
                _engine.ReturnToTitle();
            }
            else
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                window?.Close();
            }
            e.Handled = true;
            return;
        }

        if (_engine.State == GameState.Paused)
        {
            _engine.Resume();
            e.Handled = true;
            return;
        }

        _engine.Input.OnKeyDown(e.Key);
        e.Handled = true;
    }

    protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
    {
        _engine.Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Left);
        if (props.IsRightButtonPressed)
            _engine.Input.OnMouseDown(Avalonia.Input.MouseButton.Right);
        e.Handled = true;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Left);
        if (!props.IsRightButtonPressed)
            _engine.Input.OnMouseUp(Avalonia.Input.MouseButton.Right);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        _engine.Input.OnMouseWheel(e.Delta.Y);
        e.Handled = true;
    }

    private void TakeScreenshot()
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            return;

        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(this);

        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var filename = Path.Combine(picturesPath, $"AVACastle_{timestamp}.png");

        bitmap.Save(filename);
    }

    private void GameLoop(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double deltaTime = (now - _lastUpdate).TotalSeconds;
        _lastUpdate = now;

        deltaTime = Math.Min(deltaTime, 0.1);

        // Poll controller state
        _engine.Input.Update();

        // Handle controller pause/resume (with edge detection)
        HandleControllerPause();

        _engine.Update(deltaTime);
        _engine.Input.PostUpdate();
        InvalidateVisual();
    }

    private void HandleControllerPause()
    {
        // Start button toggles pause (edge detection)
        bool pausePressed = _engine.Input.IsPausePressed;
        if (pausePressed && !_wasPausePressed)
        {
            if (_engine.State == GameState.Playing)
                _engine.Pause();
            else if (_engine.State == GameState.Paused)
                _engine.Resume();
        }
        _wasPausePressed = pausePressed;

        // Back button exits when paused (edge detection)
        bool backPressed = _engine.Input.IsBackPressed;
        if (backPressed && !_wasBackPressed && _engine.State == GameState.Paused)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            window?.Close();
        }
        _wasBackPressed = backPressed;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // Fit the fixed 4:3 playfield to the window, centred, with black bars on the long sides.
        double scale = Math.Min(Bounds.Width / GameEngine.Width, Bounds.Height / GameEngine.Height);
        if (scale <= 0) return;
        double offsetX = (Bounds.Width - GameEngine.Width * scale) / 2;
        double offsetY = (Bounds.Height - GameEngine.Height * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (offsetX > 1 || offsetY > 1)
                context.DrawRectangle(BorderPen, new Rect(0, 0, GameEngine.Width, GameEngine.Height));

            DrawStars(context);
            DrawCastle(context);

            foreach (var fireball in _engine.Fireballs)
                DrawGlowPolygon(context, fireball.GetPolygonPoints(), FireballColor, 1, 2);

            foreach (var mine in _engine.Mines)
            {
                double alpha = mine.IsFading && (int)(DateTime.Now.Millisecond / 80) % 2 == 0 ? 0.35 : 1;
                foreach (var (a, b) in mine.GetLines())
                    DrawGlowLine(context, a, b, MineColor, alpha, 1.5);
            }

            foreach (var explosion in _engine.Explosions)
            {
                double alpha = explosion.GetOpacity();
                foreach (var (a, b) in explosion.GetParticleLines())
                    DrawGlowLine(context, a, b, explosion.Color, alpha, 2);
            }

            foreach (var bullet in _engine.Bullets)
                context.DrawEllipse(BulletBrush, null, bullet.Position, 2.5, 2.5);

            DrawShip(context);
            DrawHud(context);
        }
    }

    private void DrawStars(DrawingContext context)
    {
        double t = DateTime.Now.TimeOfDay.TotalSeconds;
        for (int i = 0; i < _engine.Stars.Count; i++)
        {
            var (position, brightness) = _engine.Stars[i];
            double twinkle = 0.75 + 0.25 * Math.Sin(t * (0.7 + i % 5 * 0.3) + i);
            byte a = (byte)(255 * brightness * twinkle);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(a, 170, 190, 255)),
                new Rect(position.X - 0.75, position.Y - 0.75, 1.5, 1.5));
        }
    }

    private void DrawCastle(DrawingContext context)
    {
        var castle = _engine.Castle;
        if (castle == null)
        {
            // Shown while the blast clears, then gone for the last second before the next castle.
            if (_engine.State == GameState.Playing && _engine.CastleRebuildTimer > 1.0)
                DrawCentredText(context, "CASTLE DESTROYED", 30, GameEngine.Center.Y - 15, Color.FromRgb(255, 225, 74));
            return;
        }

        // Colour comes from where a ring is on screen, like the cabinet's overlay, not from the ring.
        for (int slot = 0; slot < castle.Rings.Count; slot++)
        {
            var ring = castle.Rings[slot];
            var color = GameEngine.SlotColors[slot];
            for (int i = 0; i < Ring.SegmentCount; i++)
            {
                int health = ring.Health[i];
                if (health <= 0) continue;
                var (a, b) = ring.GetSegment(GameEngine.Center, i);
                if (health == Ring.SegmentStrength)
                    DrawGlowLine(context, a, b, color, 1, 3);
                else
                    DrawGlowLine(context, a, b, color, 0.45, 1.5);
            }
        }

        // The cannon brightens as it charges a shot.
        var cannonColor = Lerp(GameEngine.SlotColors[2], Colors.White, castle.Charge);
        DrawGlowPolygon(context, castle.GetCannonPoints(), cannonColor, 1, 2 + castle.Charge * 1.5);
    }

    private void DrawShip(DrawingContext context)
    {
        if (!_engine.ShipAlive || _engine.State is GameState.StartScreen or GameState.GameOver)
            return;

        var ship = _engine.Ship;
        if (ship.IsInvincible && (int)(DateTime.Now.Millisecond / 100) % 2 != 0)
            return;

        DrawGlowPolygon(context, ship.GetPolygonPoints(), ShipColor, 1, 2);

        if (_engine.IsThrusting && (int)(DateTime.Now.Millisecond / 50) % 2 == 0)
        {
            var flame = ship.GetThrustFlamePoints();
            DrawGlowLine(context, flame[0], flame[1], Color.FromRgb(255, 170, 80), 1, 1.5);
            DrawGlowLine(context, flame[1], flame[2], Color.FromRgb(255, 170, 80), 1, 1.5);
        }
    }

    // A vector-monitor look: each line is drawn once wide and faint, then again thin and bright.
    private void DrawGlowLine(DrawingContext context, Point a, Point b, Color color, double alpha, double width)
    {
        context.DrawLine(GetPen(color, alpha * 0.22, width * 3.5), a, b);
        context.DrawLine(GetPen(color, alpha, width), a, b);
    }

    private void DrawGlowPolygon(DrawingContext context, Point[] points, Color color, double alpha, double width)
    {
        for (int i = 0; i < points.Length; i++)
            DrawGlowLine(context, points[i], points[(i + 1) % points.Length], color, alpha, width);
    }

    private IPen GetPen(Color color, double alpha, double width)
    {
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        var key = (color, a, width);
        if (!_pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B)), width, lineCap: PenLineCap.Round);
            if (_pens.Count > 2000) _pens.Clear();
            _pens[key] = pen;
        }
        return pen;
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    private static FormattedText Text(string text, double size, Color color) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudFont, size, new SolidColorBrush(color));

    private static void DrawCentredText(DrawingContext context, string text, double size, double y, Color color)
    {
        var formatted = Text(text, size, color);
        context.DrawText(formatted, new Point((GameEngine.Width - formatted.Width) / 2, y));
    }

    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.StartScreen)
        {
            DrawStartScreen(context);
            return;
        }

        if (_engine.State == GameState.HighScores)
        {
            DrawHighScoreTable(context);
            return;
        }

        context.DrawText(Text($"SCORE {_engine.Score}", 22, TextColor), new Point(20, 16));
        // A demo's score is the pilot's, not the player's: it never shows as the high score.
        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawCentredText(context, $"HIGH {high}", 18, 18, TextColor);

        var level = Text($"CASTLE {_engine.Level}", 22, TextColor);
        context.DrawText(level, new Point(GameEngine.Width - level.Width - 20, 16));

        // Spare ships, drawn as little ship icons under the score.
        for (int i = 0; i < _engine.Lives - (_engine.ShipAlive ? 1 : 0) && i < 8; i++)
        {
            var icon = new Ship(new Point(32 + i * 26, 62), -Math.PI / 2);
            DrawGlowPolygon(context, icon.GetPolygonPoints(), ShipColor, 0.8, 1.5);
        }

        if (_engine.Sound.IsMuted)
        {
            var muted = Text("MUTED", 14, TextColor);
            context.DrawText(muted, new Point(GameEngine.Width - muted.Width - 20, 46));
        }

        if (_engine.State == GameState.Attract)
        {
            DrawCentredText(context, "DEMO", 40, 70, Color.FromRgb(255, 225, 74));
            if ((int)(DateTime.Now.TimeOfDay.TotalSeconds * 2) % 2 == 0)
                DrawCentredText(context, "Press SPACE or X to play", 26, GameEngine.Height - 80, Colors.White);
        }

        if (_engine.State == GameState.Paused)
        {
            DrawCentredText(context, "PAUSED", 48, GameEngine.Height / 2 - 230, Colors.White);
            DrawCentredText(context, "ESC to quit, any other key to resume", 20, GameEngine.Height / 2 - 170, TextColor);
        }

        if (_engine.State == GameState.GameOver)
        {
            DrawCentredText(context, "GAME OVER", 52, GameEngine.Height / 2 - 250, Color.FromRgb(255, 60, 60));
            if (_engine.IsEntryPending)
                DrawCentredText(context, NewEntryHeadline, 22, GameEngine.Height / 2 - 185, Color.FromRgb(255, 225, 74));
            else
                DrawCentredText(context, "Press SPACE or X to play again", 22, GameEngine.Height / 2 + 180, TextColor);
        }

        if (_engine.State == GameState.EnterInitials)
            DrawInitialsEntry(context);
    }

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private static readonly Color Yellow = Color.FromRgb(255, 225, 74);

    // A dark panel over the turning castle, for the entry and table screens.
    private void DrawPanel(DrawingContext context, Rect rect)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)), rect);
        DrawGlowPolygon(context, new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }, Yellow, 0.6, 1.5);
    }

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Yellow);
        DrawCentredText(context, $"SCORE {_engine.Score}    CASTLE {_engine.Level}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS", 24, 260, Colors.White);

        bool blinkOn = (int)(DateTime.Now.TimeOfDay.TotalSeconds * 3) % 2 == 0;
        const double cell = 100;
        double left = (GameEngine.Width - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Yellow);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, TextColor);
                var down = Text("v", 22, TextColor);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, TextColor);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, Yellow);

        const double rankX = 250, nameX = 340, scoreRight = 640, castleX = 690;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        var scoreHead = Text("SCORE", 16, TextColor);
        context.DrawText(scoreHead, new Point(scoreRight - scoreHead.Width, y));
        context.DrawText(Text("CASTLE", 16, TextColor), new Point(castleX, y));

        bool blinkOn = (int)(DateTime.Now.TimeOfDay.TotalSeconds * 3) % 2 == 0;
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Yellow) : i == 0 ? Yellow : Colors.White;
            double alpha = highlight || i < 3 ? 1 : 0.85;
            color = Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B);

            string rank = $"{i + 1}.";
            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string castle = i < entries.Count && entries[i].Castle > 0 ? entries[i].Castle.ToString() : "-";

            context.DrawText(Text(rank, 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            var scoreText = Text(score, 24, color);
            context.DrawText(scoreText, new Point(scoreRight - scoreText.Width, y));
            context.DrawText(Text(castle, 24, color), new Point(castleX + 20, y));

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if ((int)(DateTime.Now.TimeOfDay.TotalSeconds * 2) % 2 == 0)
            DrawCentredText(context, "Press SPACE or X to play", 24, 650, Colors.White);
    }

    private void DrawStartScreen(DrawingContext context)
    {
        DrawCentredText(context, "AVACASTLE", 64, 30, Color.FromRgb(255, 225, 74));
        DrawCentredText(context, "Break through the rings. Destroy the cannon.", 18, 112, TextColor);

        string[] controls =
        {
            "CONTROLS",
            "",
            "A / LEFT      Rotate left",
            "D / RIGHT     Rotate right",
            "W / UP        Thrust",
            "SPACE         Fire",
            "",
            "Mouse: left fires,",
            "right thrusts, wheel turns",
            "",
            "Gamepad: stick turns",
            "and thrusts, X/B fires",
            "",
            "ESC  Pause    M  Mute",
            "CTRL+S  Screenshot"
        };
        string[] scoring =
        {
            "SCORING",
            "",
            "Outer ring       10",
            "Middle ring      20",
            "Inner ring       30",
            $"Cannon         {GameEngine.CannonScoreValue}",
            "  + an extra ship",
            "",
            "Each segment takes",
            "two hits. Clear the",
            "outer ring and the",
            "rings move out while",
            "a new one grows.",
            "",
            "Mines can be shot."
        };

        DrawColumn(context, controls, 40);
        DrawColumn(context, scoring, GameEngine.Width - 300);

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 20, GameEngine.Height - 120, TextColor);

        if ((int)(DateTime.Now.TimeOfDay.TotalSeconds * 2) % 2 == 0)
            DrawCentredText(context, "Press SPACE or X to start", 28, GameEngine.Height - 80, Colors.White);
    }

    private static void DrawColumn(DrawingContext context, string[] lines, double x)
    {
        double y = 200;
        foreach (var line in lines)
        {
            bool heading = line is "CONTROLS" or "SCORING";
            context.DrawText(Text(line, heading ? 20 : 15, heading ? Colors.White : TextColor), new Point(x, y));
            y += heading ? 28 : 21;
        }
    }
}
