using System.Numerics;

namespace MotionLab;

internal static class RearHairFlow
{
    internal const float MaximumOffset=14;
    internal const float ExtensionStart=1198,ExtensionBottom=1414,DisplayBottom=1254;
    internal static float Amount(float y,Rig rig)
    {var t=rig.RearBand.Linear(y);return t*t;}
    internal static float Offset(float sway,float tip)=>Math.Clamp(-10*(.7f*sway+.3f*tip),-MaximumOffset,MaximumOffset);
    internal static Vector2 Deform(Vector2 point,Rig rig)=>
        // Bend in head-local space, then inherit the complete parent transform, including the tips.
        Vector2.Transform(point+new Vector2(rig.RearOffset*Amount(point.Y,rig),0),rig.Skin[1]);
}
