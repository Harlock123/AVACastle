using System;
using Avalonia;

namespace AVACastle.Models;

/// <summary>The cannon's shot: a big, slow, spinning starburst that flies straight and doesn't wrap.</summary>
public class Fireball : GameObject
{
    private double _spin;

    public Fireball(Point position, double direction, double speed) : base(position, 11)
    {
        Velocity = new Vector(Math.Cos(direction) * speed, Math.Sin(direction) * speed);
    }

    public override void Update(double deltaTime, double screenWidth, double screenHeight)
    {
        Position = new Point(Position.X + Velocity.X * deltaTime, Position.Y + Velocity.Y * deltaTime);
        _spin += deltaTime * 9;

        if (Position.X < -Radius || Position.X > screenWidth + Radius ||
            Position.Y < -Radius || Position.Y > screenHeight + Radius)
        {
            IsAlive = false;
        }
    }

    /// <summary>An eight-pointed star whose points pulse in and out as it spins.</summary>
    public override Point[] GetPolygonPoints()
    {
        var points = new Point[16];
        for (int i = 0; i < points.Length; i++)
        {
            double a = _spin + i * Math.PI / 8;
            double r = i % 2 == 0 ? Radius + 3 + Math.Sin(_spin * 2) * 3 : Radius * 0.45;
            points[i] = new Point(Position.X + Math.Cos(a) * r, Position.Y + Math.Sin(a) * r);
        }
        return points;
    }
}
