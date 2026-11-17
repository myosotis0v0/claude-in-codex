namespace MotionLab;

internal static class PatEyes
{
    internal const float CloseSeconds=.12f,ReleaseAt=4.32f,OpenSeconds=.24f;
    private static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    internal static float Closure(double seconds)=>Smooth((float)seconds/CloseSeconds)*(1-Smooth(((float)seconds-ReleaseAt)/OpenSeconds));
    internal static Pose Apply(Pose pose,double seconds,float blink)
    {
        var closed=Closure(seconds);
        return pose with{EyeLeft=(1-closed)*(1-blink),EyeRight=(1-closed)*(1-blink),SmileEyes=closed,SoftSmileEyes=closed};
    }
}
