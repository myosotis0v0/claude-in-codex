using System.Numerics;

namespace MotionLab;

internal static class RearHairFlow
{
    internal const float RootY=365,TipY=1218,MaximumOffset=14;
    internal static float Amount(float y)
    {var t=Math.Clamp((y-RootY)/(TipY-RootY),0,1);return t*t;}
    internal static float Offset(float sway,float tip)=>Math.Clamp(-10*(.7f*sway+.3f*tip),-MaximumOffset,MaximumOffset);
    internal static Vector2 Deform(Vector2 point,Rig rig)=>
        // Bend in head-local space, then inherit the complete parent transform, including the tips.
        Vector2.Transform(point+new Vector2(rig.RearOffset*Amount(point.Y),0),rig.Skin[1]);
}
