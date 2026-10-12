using System;
using Avalonia;

namespace AVACastle.Models;

/// <summary>
/// A homing mine. It leaves the core, passes straight through the rings, and steers after the ship
/// until it burns out.
/// </summary>
public class Mine : GameObject
{
    public const double Lifetime = 11.0;

    private readonly double _maxSpeed;
    private readonly double _turnRate;
    private double _age;
    private double _spin;

    public bool IsFading => _age > Lifetime - 1.5;

    public Mine(Point position, double direction, int level) : base(position, 8)
    {
        _maxSpeed = Math.Min(115 + 10 * (level - 1), 190);
        _turnRate = Math.Min(1.8 + 0.15 * (level - 1), 3.2);
        Velocity = new Vector(Math.Cos(direction) * 70, Math.Sin(direction) * 70);
    }

    public void Update(double deltaTime, double screenWidth, double screenHeight, Point target)
    {
        _age += deltaTime;
        _spin += deltaTime * 6;
        if (_age >= Lifetime)
        {
            IsAlive = false;
            return;
        }

        double heading = Math.Atan2(Velocity.Y, Velocity.X);
        double wanted = Math.Atan2(target.Y - Position.Y, target.X - Position.X);
        double diff = Math.IEEERemainder(wanted - heading, Math.PI * 2);
        heading += Math.Clamp(diff, -_turnRate * deltaTime, _turnRate * deltaTime);

        double speed = Math.Min(Velocity.Length + 60 * deltaTime, _maxSpeed);
        Velocity = new Vector(Math.Cos(heading) * speed, Math.Sin(heading) * speed);

        base.Update(deltaTime, screenWidth, screenHeight);
    }

    /// <summary>Two crossed diamonds that spin in opposite directions.</summary>
    public (Point Start, Point End)[] GetLines()
    {
        var lines = new (Point, Point)[8];
        for (int shape = 0; shape < 2; shape++)
        {
            double rot = shape == 0 ? _spin : -_spin + Math.PI / 4;
            double r = shape == 0 ? Radius : Radius * 0.6;
            for (int i = 0; i < 4; i++)
            {
                double a0 = rot + i * Math.PI / 2, a1 = a0 + Math.PI / 2;
                lines[shape * 4 + i] = (
                    new Point(Position.X + Math.Cos(a0) * r, Position.Y + Math.Sin(a0) * r),
                    new Point(Position.X + Math.Cos(a1) * r, Position.Y + Math.Sin(a1) * r));
            }
        }
        return lines;
    }

    public override Point[] GetPolygonPoints() => Array.Empty<Point>();
}
