namespace MotionLab;

internal readonly record struct FrontHairPose(float Left,float LeftTip,float Center,float CenterTip,float Right,float RightTip);

internal sealed class FrontHairDynamics
{
    private readonly Spring left=new(36,9),leftTip=new(22,7),center=new(53,12),centerTip=new(34,9),right=new(43,10),rightTip=new(27,8);
    internal FrontHairPose Current {get;private set;}
    internal FrontHairPose Step(float force,float dt)
    {
        var l=left.Step(force*1.1f,dt);var c=center.Step(force*.62f,dt);var r=right.Step(force*.9f,dt);
        var lt=leftTip.Step(l,dt);var ct=centerTip.Step(c,dt);var rt=rightTip.Step(r,dt);
        Current=new(Math.Clamp(l,-2.5f,2.5f),Math.Clamp(lt,-2.5f,2.5f),Math.Clamp(c,-1.4f,1.4f),Math.Clamp(ct,-1.4f,1.4f),Math.Clamp(r,-2.2f,2.2f),Math.Clamp(rt,-2.2f,2.2f));
        return Current;
    }
}
