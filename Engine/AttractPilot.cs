using System;
using System.Linq;
using Avalonia;
using AVACastle.Models;

namespace AVACastle.Engine;

/// <summary>What the ship is told to do this frame, by the player's input or by the attract-mode pilot.</summary>
public readonly record struct ShipControls(bool RotateLeft, bool RotateRight, bool Thrust, bool Fire);

/// <summary>
/// Flies the ship in attract mode. It holds a stand-off orbit round the castle, slides out of the
/// cannon's line when it charges, turns on mines that get close, and otherwise hammers away at the
/// rings with a slowly sweeping aim. Good enough to look like someone who knows the game, not so good
/// that it never dies.
/// </summary>
public class AttractPilot
{
    private const double OrbitRadius = 250;
    private const double OrbitDrift = 0.12;        // radians per second round the castle
    private const double CruiseSpeed = 110;
    private const double MineThreatRange = 170;
    private const double AimTolerance = 0.07;
    private const double TurnTolerance = 0.05;

    private static readonly Random Random = new();

    private double _orbitAngle;
    private double _sweepClock;
    private bool _fireToggle;
    private double _dodgeSide = 1;
    private double _hesitation;

    public void Reset(Point shipPosition)
    {
        _orbitAngle = Math.Atan2(shipPosition.Y - GameEngine.Center.Y, shipPosition.X - GameEngine.Center.X);
        _dodgeSide = Random.Next(2) == 0 ? 1 : -1;
        _hesitation = 0;
    }

    public ShipControls Fly(GameEngine engine, double deltaTime)
    {
        if (!engine.ShipAlive)
            return default;

        var ship = engine.Ship;
        var center = GameEngine.Center;
        _sweepClock += deltaTime;
        _orbitAngle += OrbitDrift * deltaTime;

        // Now and then it lets its guard down for a moment, which is when the castle gets it.
        _hesitation -= deltaTime;
        if (_hesitation < -6 && Random.NextDouble() < deltaTime * 0.15)
            _hesitation = 0.6 + Random.NextDouble() * 0.6;
        bool alert = _hesitation <= 0;

        // Where it wants to be: on the orbit, pushed away from mines and out of the cannon's line.
        var goal = new Point(center.X + Math.Cos(_orbitAngle) * OrbitRadius, center.Y + Math.Sin(_orbitAngle) * OrbitRadius);
        var wanted = Clamp(new Vector(goal.X - ship.Position.X, goal.Y - ship.Position.Y) * 1.2, CruiseSpeed);

        if (alert)
        {
            foreach (var mine in engine.Mines)
            {
                var away = new Vector(ship.Position.X - mine.Position.X, ship.Position.Y - mine.Position.Y);
                double d = away.Length;
                if (d < 140 && d > 1)
                    wanted += away / d * (140 - d) * 2.2;
            }

            var castle = engine.Castle;
            bool threatened = engine.Fireballs.Any(f => IsComingAt(f, ship.Position)) ||
                              castle is { Charge: > 0 };
            if (threatened)
            {
                // Sidestep at right angles to the line from the castle.
                double a = Math.Atan2(ship.Position.Y - center.Y, ship.Position.X - center.X) + _dodgeSide * Math.PI / 2;
                wanted += new Vector(Math.Cos(a), Math.Sin(a)) * 260;
            }
        }

        var error = wanted - ship.Velocity;
        bool needsToMove = error.Length > (alert ? 70 : 160);

        // A mine close by and closing gets shot before anything else.
        var mineTarget = engine.Mines
            .Where(m => Distance(m.Position, ship.Position) < MineThreatRange)
            .OrderBy(m => Distance(m.Position, ship.Position))
            .Select(m => (Point?)m.Position)
            .FirstOrDefault();

        double heading;
        bool shoot = false, thrust = false;

        if (needsToMove && alert)
        {
            heading = Math.Atan2(error.Y, error.X);
            thrust = Math.Abs(AngleDiff(heading, ship.Rotation)) < 0.35;
        }
        else
        {
            Point target = mineTarget ?? center;
            heading = Math.Atan2(target.Y - ship.Position.Y, target.X - ship.Position.X);
            if (mineTarget == null)
                heading += Math.Sin(_sweepClock * 1.3) * 0.16; // sweep across the rings to cut a channel

            shoot = Math.Abs(AngleDiff(heading, ship.Rotation)) < AimTolerance;
        }

        double diff = AngleDiff(heading, ship.Rotation);

        // Fire is edge-triggered, so the pilot taps it on and off like a player would.
        _fireToggle = !_fireToggle;
        return new ShipControls(
            RotateLeft: diff < -TurnTolerance,
            RotateRight: diff > TurnTolerance,
            Thrust: thrust,
            Fire: shoot && _fireToggle);
    }

    /// <summary>Called when the cannon has fired, so the next dodge may go the other way.</summary>
    public void OnCannonFired() => _dodgeSide = Random.Next(2) == 0 ? 1 : -1;

    private static bool IsComingAt(Fireball fireball, Point target)
    {
        var toTarget = new Vector(target.X - fireball.Position.X, target.Y - fireball.Position.Y);
        double d = toTarget.Length;
        if (d > 450 || d < 1) return false;
        double speed = fireball.Velocity.Length;
        if (speed < 1) return false;
        double cos = (toTarget.X * fireball.Velocity.X + toTarget.Y * fireball.Velocity.Y) / (d * speed);
        double missBy = d * Math.Sqrt(Math.Max(0, 1 - cos * cos));
        return cos > 0 && missBy < 45;
    }

    private static Vector Clamp(Vector v, double max)
    {
        double length = v.Length;
        return length > max ? v / length * max : v;
    }

    private static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
