using System;
using Avalonia;

namespace AVACastle.Models;

/// <summary>
/// One of the castle's three energy rings: a rotating 12-sided polygon whose sides (segments) each
/// take two hits to destroy.
/// </summary>
public class Ring
{
    public const int SegmentCount = 12;
    public const int SegmentStrength = 2;
    private const double SegmentArc = Math.PI * 2 / SegmentCount;

    // Rings take the same time to reach their slots whatever the distance, so a castle growing out of
    // the core stays nested instead of the rings sliding over each other.
    private const double GrowTime = 1.2;
    private double _growSpeed;
    private double _targetRadius;

    public int[] Health { get; } = new int[SegmentCount];
    public double Radius { get; private set; }
    public double TargetRadius
    {
        get => _targetRadius;
        set
        {
            _targetRadius = value;
            _growSpeed = Math.Max(30, Math.Abs(value - Radius) / GrowTime);
        }
    }
    public double Rotation { get; private set; }
    public double AngularSpeed { get; set; }

    public bool IsEmpty => Array.TrueForAll(Health, h => h <= 0);
    public bool IsGrowing => Math.Abs(Radius - TargetRadius) > 0.5;

    public Ring(double radius, double startRadius, double angularSpeed)
    {
        Radius = startRadius;
        TargetRadius = radius;
        AngularSpeed = angularSpeed;
        Rotation = Random.Shared.NextDouble() * SegmentArc;
        Array.Fill(Health, SegmentStrength);
    }

    public void Update(double deltaTime)
    {
        Rotation = NormalizeAngle(Rotation + AngularSpeed * deltaTime);

        double diff = TargetRadius - Radius;
        double step = _growSpeed * deltaTime;
        Radius = Math.Abs(diff) <= step ? TargetRadius : Radius + Math.Sign(diff) * step;
    }

    /// <summary>The segment under a direction (radians, screen angle) from the castle's centre.</summary>
    public int SegmentAt(double angle)
    {
        int index = (int)(NormalizeAngle(angle - Rotation) / SegmentArc);
        return Math.Clamp(index, 0, SegmentCount - 1);
    }

    public bool IsIntactAt(double angle) => Health[SegmentAt(angle)] > 0;

    /// <summary>
    /// The ring's distance from the centre in a given direction. The sides are straight chords, so
    /// mid-side is a little closer in than the corners.
    /// </summary>
    public double RadiusAt(double angle)
    {
        double local = NormalizeAngle(angle - Rotation) % SegmentArc - SegmentArc / 2;
        return Radius * Math.Cos(SegmentArc / 2) / Math.Cos(local);
    }

    public (Point Start, Point End) GetSegment(Point center, int index)
    {
        double a0 = Rotation + index * SegmentArc;
        double a1 = a0 + SegmentArc;
        return (
            new Point(center.X + Math.Cos(a0) * Radius, center.Y + Math.Sin(a0) * Radius),
            new Point(center.X + Math.Cos(a1) * Radius, center.Y + Math.Sin(a1) * Radius)
        );
    }

    public static double NormalizeAngle(double angle)
    {
        angle %= Math.PI * 2;
        return angle < 0 ? angle + Math.PI * 2 : angle;
    }
}
