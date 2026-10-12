using System;
using System.Collections.Generic;
using Avalonia;

namespace AVACastle.Models;

/// <summary>
/// The star castle: a cannon at the centre of three counter-rotating rings. The cannon turns to follow
/// the ship and fires when it has a clear line through all three rings.
/// </summary>
public class Castle
{
    // Ring slots, outermost first. A ring keeps its slot's radius, spin and points; when the outer
    // ring is cleared the other two move out a slot and a new ring grows from the core.
    public static readonly double[] SlotRadius = { 132, 106, 80 };
    private static readonly double[] SlotSpin = { 0.45, -0.65, 0.85 };
    public static readonly int[] SlotPoints = { 10, 20, 30 };

    public const double CannonRadius = 20;
    private const double ChargeTime = 0.2;

    public Point Center { get; }
    public int Level { get; }
    public List<Ring> Rings { get; } = new();
    public double CannonRotation { get; private set; }

    /// <summary>0 when idle, rising to 1 as the cannon charges a shot.</summary>
    public double Charge => _charging ? Math.Min(_chargeTimer / ChargeTime, 1) : 0;

    private double _fireCooldown;
    private double _chargeTimer;
    private bool _charging;

    private double SpinScale => 1 + 0.12 * (Level - 1);
    private double TurnSpeed => 2.2 + 0.2 * (Level - 1);
    private double FireInterval => Math.Max(0.8, 1.8 - 0.1 * (Level - 1));
    public double FireballSpeed => 210 + 20 * (Level - 1);

    public Castle(Point center, int level, bool growFromCore = true)
    {
        Center = center;
        Level = level;
        CannonRotation = Math.PI / 2;
        _fireCooldown = FireInterval;

        for (int slot = 0; slot < SlotRadius.Length; slot++)
        {
            double start = growFromCore ? CannonRadius : SlotRadius[slot];
            Rings.Add(new Ring(SlotRadius[slot], start, SlotSpin[slot] * SpinScale));
        }
    }

    public bool IsBuilding => Rings.Exists(r => r.IsGrowing);

    public void Update(double deltaTime)
    {
        foreach (var ring in Rings)
            ring.Update(deltaTime);
    }

    /// <summary>
    /// Turns the cannon toward the target and returns a fireball when it fires. It only fires once
    /// aimed with an open path through every ring, after a short, visible charge.
    /// </summary>
    public Fireball? UpdateCannon(double deltaTime, Point target, bool canFire)
    {
        double wanted = Math.Atan2(target.Y - Center.Y, target.X - Center.X);
        double diff = Math.IEEERemainder(wanted - CannonRotation, Math.PI * 2);
        double step = TurnSpeed * deltaTime;
        CannonRotation += Math.Abs(diff) <= step ? diff : Math.Sign(diff) * step;

        _fireCooldown -= deltaTime;

        // It needs an open line to the target, and to be roughly on it: the shot goes where it points.
        bool ready = canFire && _fireCooldown <= 0 && !IsBuilding &&
                     Math.Abs(diff) < 0.2 && HasClearPath(wanted);

        if (!ready)
        {
            _charging = false;
            _chargeTimer = 0;
            return null;
        }

        _charging = true;
        _chargeTimer += deltaTime;
        if (_chargeTimer < ChargeTime)
            return null;

        _charging = false;
        _chargeTimer = 0;
        _fireCooldown = FireInterval;

        var muzzle = new Point(
            Center.X + Math.Cos(CannonRotation) * (CannonRadius + 6),
            Center.Y + Math.Sin(CannonRotation) * (CannonRadius + 6));
        return new Fireball(muzzle, wanted, FireballSpeed);
    }

    /// <summary>True when no ring has a standing segment in this direction.</summary>
    public bool HasClearPath(double angle)
    {
        foreach (var ring in Rings)
        {
            if (ring.IsIntactAt(angle))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The outer ring is gone: the middle and inner rings move out a slot each and a fresh inner ring
    /// grows out of the core.
    /// </summary>
    public void ShiftRings()
    {
        Rings.RemoveAt(0);
        for (int slot = 0; slot < Rings.Count; slot++)
        {
            Rings[slot].TargetRadius = SlotRadius[slot];
            Rings[slot].AngularSpeed = SlotSpin[slot] * SpinScale;
        }

        int inner = Rings.Count;
        Rings.Add(new Ring(SlotRadius[inner], CannonRadius, SlotSpin[inner] * SpinScale));
    }

    public Point[] GetCannonPoints()
    {
        // A squat hull with a long barrel, pointing along CannonRotation.
        var shape = new (double Angle, double Length)[]
        {
            (0, CannonRadius + 12),
            (0.28, CannonRadius - 2),
            (1.2, CannonRadius - 6),
            (2.3, CannonRadius - 4),
            (Math.PI, CannonRadius - 10),
            (-2.3, CannonRadius - 4),
            (-1.2, CannonRadius - 6),
            (-0.28, CannonRadius - 2)
        };

        var points = new Point[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            double a = CannonRotation + shape[i].Angle;
            points[i] = new Point(Center.X + Math.Cos(a) * shape[i].Length, Center.Y + Math.Sin(a) * shape[i].Length);
        }
        return points;
    }
}
