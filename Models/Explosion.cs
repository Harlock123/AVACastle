using System;
using Avalonia;
using Avalonia.Media;

namespace AVACastle.Models;

public class Explosion
{
    private static readonly Random Random = new();

    public Point Position { get; }
    public Color Color { get; }
    public double Duration { get; }
    public double TimeElapsed { get; private set; }
    public bool IsFinished => TimeElapsed >= Duration;
    public double Progress => Math.Min(TimeElapsed / Duration, 1.0);

    private readonly ExplosionParticle[] _particles;

    private Explosion(Point position, Color color, int particleCount, double baseSpeed, double duration, double maxLength)
    {
        Position = position;
        Color = color;
        Duration = duration;

        _particles = new ExplosionParticle[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            _particles[i] = new ExplosionParticle
            {
                Angle = Random.NextDouble() * Math.PI * 2,
                Speed = baseSpeed * (0.4 + Random.NextDouble()),
                Length = maxLength * (0.4 + Random.NextDouble() * 0.6),
                Offset = Random.NextDouble() * 5
            };
        }
    }

    /// <summary>A ring segment shattering.</summary>
    public static Explosion CreateSegment(Point position, Color color) => new(position, color, 7, 90, 0.35, 8);

    public static Explosion CreateMine(Point position) => new(position, Color.FromRgb(160, 255, 190), 10, 120, 0.4, 8);

    public static Explosion CreateShip(Point position) => new(position, Colors.White, 28, 190, 0.9, 16);

    /// <summary>The cannon going up: a long, slow, heavy blast.</summary>
    public static Explosion CreateCastle(Point position, Color color) => new(position, color, 60, 260, 2.2, 28);

    public void Update(double deltaTime)
    {
        TimeElapsed += deltaTime;
    }

    public (Point Start, Point End)[] GetParticleLines()
    {
        var lines = new (Point Start, Point End)[_particles.Length];
        double progress = Progress;

        for (int i = 0; i < _particles.Length; i++)
        {
            var p = _particles[i];
            double distance = p.Speed * TimeElapsed * (1 - progress * 0.4) + p.Offset;
            double endDist = distance + p.Length * (1 - progress);

            lines[i] = (
                new Point(Position.X + Math.Cos(p.Angle) * distance, Position.Y + Math.Sin(p.Angle) * distance),
                new Point(Position.X + Math.Cos(p.Angle) * endDist, Position.Y + Math.Sin(p.Angle) * endDist));
        }

        return lines;
    }

    public double GetOpacity() => 1.0 - Progress;

    private struct ExplosionParticle
    {
        public double Angle;
        public double Speed;
        public double Length;
        public double Offset;
    }
}
