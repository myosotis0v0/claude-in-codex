namespace MotionLab;

internal static class MotionLayers
{
    internal const float AmbientAmplitude=1.2f;
    internal static float Ambient(double globalSeconds)=>AmbientAmplitude*(float)(Math.Sin(2*Math.PI*globalSeconds/6)+.16*Math.Sin(4*Math.PI*globalSeconds/6));
    internal static float AuthoredHairWeight(string id,double localSeconds)=>id switch
    {
        "breeze"=>1,
        "shake" or "pat" or "nod"=>Motions.Envelope(localSeconds/Motions.Duration),
        _=>0
    };
    internal static Pose Evaluate(string id,double localSeconds,double globalSeconds,bool ambientEnabled=true)
    {
        var pose=Motions.Evaluate(id,localSeconds);
        return pose with{AmbientWind=ambientEnabled?Ambient(globalSeconds)*(1-AuthoredHairWeight(id,localSeconds)):0};
    }
}
