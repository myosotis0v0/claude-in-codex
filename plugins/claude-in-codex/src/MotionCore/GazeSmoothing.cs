namespace MotionLab;

internal static class GazeSmoothing
{
    internal static double MaxSpeed(bool fast)=>fast?7:5;

    internal static double Fraction(double distance,double elapsed,bool fast=false)
    {
        if(distance<=0)return 0;
        double time=AnimationCurve.FrameDelta(elapsed);
        double speed=MaxSpeed(fast),response=fast?12:8;
        double dampedRadius=speed/response;
        // Advance at the speed cap only until the exponential damping can take over.
        double cappedTime=Math.Min(time,Math.Max(0,(distance-dampedRadius)/speed));
        double remaining=(distance-speed*cappedTime)*Math.Exp(-response*(time-cappedTime));
        return Math.Clamp(1-remaining/distance,0,1);
    }
}
