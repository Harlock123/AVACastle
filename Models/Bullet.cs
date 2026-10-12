using System;
using Avalonia;

namespace AVACastle.Models;

public class Bullet : GameObject
{
    public const double Speed = 520.0;
    public const double MaxLifetime = 1.1;

    private double _lifetime;

    // Where the bullet was before this frame's move, so ring hits can be found by the path it swept
    // rather than by where it happened to land (a bullet moves ~9 px a frame; a ring is a thin line).
    public Point PreviousPosition { get; private set; }

    public Bullet(Point position, double direction) : base(position, 3)
    {
        Rotation = direction;
        Velocity = new Vector(
            Math.Cos(direction) * Speed,
            Math.Sin(direction) * Speed
        );
        PreviousPosition = position;
        _lifetime = MaxLifetime;
    }

    public override void Update(double deltaTime, double screenWidth, double screenHeight)
    {
        PreviousPosition = Position;
        base.Update(deltaTime, screenWidth, screenHeight);

        // A wrap teleports the bullet: don't treat the jump as a swept path.
        if (Math.Abs(Position.X - PreviousPosition.X) > 100 || Math.Abs(Position.Y - PreviousPosition.Y) > 100)
            PreviousPosition = Position;

        _lifetime -= deltaTime;
        if (_lifetime <= 0)
        {
            IsAlive = false;
        }
    }

    public override Point[] GetPolygonPoints()
    {
        return new[]
        {
            new Point(Position.X - 2, Position.Y - 2),
            new Point(Position.X + 2, Position.Y - 2),
            new Point(Position.X + 2, Position.Y + 2),
            new Point(Position.X - 2, Position.Y + 2)
        };
    }
}
