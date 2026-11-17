using System.Windows;

namespace ClaudeCompanion;

internal readonly record struct GazeLimits(double Outward, double Inward, double Vertical);

internal static class GazeMotion
{
    internal static Vector Map(Vector target)
    {
        // Preserve direction without flat, per-axis saturation; increase near-field response.
        return target / Math.Sqrt(0.7 * 0.7 + target.LengthSquared);
    }

    internal static Vector Offset(Vector gaze, bool viewerLeft, GazeLimits limits)
    {
        var outward = viewerLeft ? gaze.X < 0 : gaze.X > 0;
        return new Vector(gaze.X * (outward ? limits.Outward : limits.Inward), gaze.Y * limits.Vertical);
    }
}
