namespace MotionLab;

internal enum CurveInterpolation { Step, Linear, Smooth }
internal readonly record struct CurveKey(double Seconds,float Value,CurveInterpolation ToNext=CurveInterpolation.Linear);

internal static class AnimationCurve
{
    // Keys are sorted by time. Interpolation belongs to the segment's left key.
    internal static float Sample(ReadOnlySpan<CurveKey> keys,double seconds)
    {
        if(keys.IsEmpty)throw new ArgumentException("A curve needs at least one key.",nameof(keys));
        if(seconds<=keys[0].Seconds)return keys[0].Value;
        for(int i=1;i<keys.Length;i++){
            var right=keys[i];
            if(seconds>=right.Seconds)continue;
            var left=keys[i-1];
            if(left.ToNext==CurveInterpolation.Step||left.Value==right.Value)return left.Value;
            var u=(float)((seconds-left.Seconds)/(right.Seconds-left.Seconds));
            if(left.ToNext==CurveInterpolation.Smooth)u=u*u*(3-2*u);
            return left.Value+(right.Value-left.Value)*u;
        }
        return keys[^1].Value;
    }

    // Treat a wake-up or long UI stall as a pause rather than skipping an entire gesture.
    internal static double FrameDelta(double elapsed)=>elapsed>.25?.08:Math.Max(0,elapsed);
}
