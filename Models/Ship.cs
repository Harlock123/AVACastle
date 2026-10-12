using System;
using Avalonia;

namespace AVACastle.Models;

public class Ship : GameObject
{
    public const double RotationSpeed = 5.0;
    public const double ThrustPower = 280.0;
    public const double MaxSpeed = 360.0;
    public const double ShootCooldown = 0.16;
    public const int MaxBullets = 5;

    private double _shootTimer;
    public bool IsInvincible { get; set; }
    public double InvincibilityTimer { get; set; }
    public const double InvincibilityDuration = 2.0;

    public Ship(Point position, double rotation) : base(position, 12)
    {
        Rotation = rotation;
    }

    public void RotateLeft(double deltaTime)
    {
        Rotation -= RotationSpeed * deltaTime;
    }

    public void RotateRight(double deltaTime)
    {
        Rotation += RotationSpeed * deltaTime;
    }

    public void Thrust(double deltaTime)
    {
        var thrustVector = new Vector(
            Math.Cos(Rotation) * ThrustPower * deltaTime,
            Math.Sin(Rotation) * ThrustPower * deltaTime
        );

        Velocity += thrustVector;

        double speed = Math.Sqrt(Velocity.X * Velocity.X + Velocity.Y * Velocity.Y);
        if (speed > MaxSpeed)
        {
            Velocity = new Vector(
                Velocity.X / speed * MaxSpeed,
                Velocity.Y / speed * MaxSpeed
            );
        }
    }

    public override void Update(double deltaTime, double screenWidth, double screenHeight)
    {
        base.Update(deltaTime, screenWidth, screenHeight);

        if (_shootTimer > 0)
            _shootTimer -= deltaTime;

        if (IsInvincible)
        {
            InvincibilityTimer -= deltaTime;
            if (InvincibilityTimer <= 0)
            {
                IsInvincible = false;
            }
        }
    }

    public Bullet? Shoot(int bulletsInFlight)
    {
        if (_shootTimer > 0 || bulletsInFlight >= MaxBullets) return null;

        _shootTimer = ShootCooldown;

        var bulletPos = new Point(
            Position.X + Math.Cos(Rotation) * 18,
            Position.Y + Math.Sin(Rotation) * 18
        );

        return new Bullet(bulletPos, Rotation);
    }

    public void Respawn(Point position, double rotation)
    {
        Position = position;
        Velocity = new Vector(0, 0);
        Rotation = rotation;
        IsInvincible = true;
        InvincibilityTimer = InvincibilityDuration;
    }

    // The Star Castle ship: a slim arrowhead with a notched tail.
    public override Point[] GetPolygonPoints()
    {
        return new[]
        {
            Polar(0, 18),
            Polar(2.45, 13),
            Polar(Math.PI, 5),
            Polar(-2.45, 13)
        };
    }

    public Point[] GetThrustFlamePoints()
    {
        return new[]
        {
            Polar(2.8, 8),
            Polar(Math.PI, 20),
            Polar(-2.8, 8)
        };
    }

    private Point Polar(double angleOffset, double length) => new(
        Position.X + Math.Cos(Rotation + angleOffset) * length,
        Position.Y + Math.Sin(Rotation + angleOffset) * length
    );
}
