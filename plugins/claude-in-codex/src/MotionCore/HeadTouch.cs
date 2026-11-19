using System.Numerics;
namespace MotionLab;

internal readonly record struct HeadTouchPose(bool Connected,float Squash,float Sway,float Tip,
    FrontHairPose Front,float TipRoll,FrontHairPose TouchTips);

internal sealed class HeadTouch{
    private readonly float tipLever;
    internal HeadTouch(CharacterRig? profile=null){
        profile??=CharacterRig.Default;
        tipLever=profile.TouchPivot.Y-profile.Weights.TouchTip.Start;
    }
    private Vector2 point,lo,hi,lastPoint;
    private bool held;
    private double accumulator;
    internal bool Active {get;private set;}
    internal bool Connected {get;private set;}
    internal void Begin(float time,bool menuOpen=false){
        if(!menuOpen)pulse=-5;
        point=lo=hi=lastPoint=default;held=true;Active=true;Connected=false;samples.Clear();samples.Enqueue((time,point));Sample(time,point,true);
    }
    internal void Move(float t,Vector2 pos){
        if(!held)return;
        var p=pos;if(p==lastPoint)return;lastPoint=p;
        lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);
        point=Vector2.Clamp(p-(lo+hi)*.5f,new(-1),new(1));
        while(samples.Count>=128||samples.Count>1&&samples.Peek().t<t-1.25f)samples.Dequeue();
        samples.Enqueue((t,p));
        var spanX=samples.Max(a=>a.p.X)-samples.Min(a=>a.p.X);
        var spanY=samples.Max(a=>a.p.Y)-samples.Min(a=>a.p.Y);
        // Signed swept area distinguishes a loop from a back-and-forth stroke.
        var start=samples.Peek().p;var prev=Vector2.Zero;float area=0;
        foreach(var sample in samples){var q=sample.p-start;area+=prev.X*q.Y-prev.Y*q.X;prev=q;}
        if(spanX>.36f&&spanY>.32f&&Math.Abs(area)>.12f)Mode="circle";
        else if(Mode!="circle"&&spanX>.29f)Mode="rub";
        if(Mode is "rub" or "circle")Connected=true;
    }
    internal bool End(float time,bool menuOpen=false,bool moved=false){
        if(!held)return false;
        bool click=!moved&&time-pressed<.24f&&Mode=="press";
        if(click&&menuOpen)pulse=time;
        wasGesture=!click||menuOpen;held=false;Sample(time,point,false);ended=time;return click&&!menuOpen;
    }
    private float ended=-10;
    private bool wasGesture;
    internal bool Playing(float time)=>Active&&(held?(Mode!="press"||time-pressed>=.24f||time-pulse<.29f):wasGesture&&time-ended<.55f);
    internal void Step(float time,double delta){
        if(!Active)return;
        if(!held&&time-ended>=.55f){Active=false;Connected=false;return;}
        accumulator+=AnimationCurve.FrameDelta(delta);
        while(accumulator>=dt){accumulator-=dt;Sample(time-(float)accumulator,point,held);}
    }
    const float dt=1f/120;
    readonly Queue<(float t,Vector2 p)> samples=new();
    public string Mode="press";
    public float X,Y,Angle,Sway,Tip;
    float vx,vy,va,vs,vt,closure,vclose,blush,gazeX,gazeY;
    bool rubSmile,wasDown;
    float pressed,pulse=-5,vYaw,vPitch;
    public float Yaw,Pitch,Squash;
    internal float TipRoll,TipLift;
    private float vRoll,vLift;
    public FrontHairPose Front=>new(Sway*.62f,Tip*.77f,Sway*.33f,Tip*.44f,Sway*.71f,Tip*.85f);
    internal FrontHairPose TouchTips=>new(0,TipRoll+TipLift,0,.8f*TipRoll,0,TipRoll-TipLift);
    internal HeadTouchPose Snapshot()=>new(Connected,Squash,Sway,Tip,Front,TipRoll,TouchTips);
    static void Spring(ref float value,ref float v,float target,float frequency,float damping){
        v+=(frequency*frequency*(target-value)-2*damping*frequency*v)*dt;
        value+=v*dt;
    }
    public void Sample(float t,Vector2 p,bool down){
        float x=0,y=0,angle=0,eye=0,b=0,gx=0,gy=0;
        if(down&&!wasDown)pressed=t;
        wasDown=down;
        var age=t-pulse;
        float Bell(float z)=>z>=0&&z<=1?MathF.Sin(z*MathF.PI)*MathF.Sin(z*MathF.PI):0;
        Squash=.265f*Bell(age/.155f)-.047f*Bell((age-.155f)/.135f);
        float yaw=0,pitch=0;
        if(down){
            if(Mode=="rub"){angle=p.X*3.4f;eye=.99f;b=.51f;}
            else if(Mode=="circle"){yaw=p.X/.61f;pitch=p.Y/.49f;x=p.X*2;angle=1.8f*yaw;y=8+12*pitch;eye=.39f-p.Y*.25f;b=.48f;gx=p.X*1.5f;gy=p.Y*1.3f;}
            else if(t-pressed>=.24f){x=2;angle=-1.6f;y=54;eye=1;b=.54f;}
        }else{samples.Clear();Mode="press";}
        if(age<.28f){eye=Math.Max(eye,Math.Clamp(Squash*6,0,1));b=Math.Max(b,.25f);}
        Spring(ref Yaw,ref vYaw,yaw,21,.82f);Spring(ref Pitch,ref vPitch,pitch,21,.82f);
        Spring(ref X,ref vx,x,18,.83f);Spring(ref Y,ref vy,y,17,down?.72f:.50f);
        // A stroke drives the target angle; only the settling motion is time-based.
        Spring(ref Angle,ref va,angle,Mode=="rub"?13:17,Mode=="rub"||!down?.61f:.78f);
        // A lock hangs below its attachment: positive local roll lags a rightward crown.
        float attachmentVx=vx+tipLever*MathF.PI/180*va;
        Spring(ref TipRoll,ref vRoll,Connected?Math.Clamp(.085f*attachmentVx,-12,12):0,22,.78f);
        Spring(ref TipLift,ref vLift,Connected?Math.Clamp(.036f*vy,-4,4):0,20,.8f);
        Spring(ref Sway,ref vs,-va*.12f-vy*.018f-vYaw*.42f,16,.66f);Spring(ref Tip,ref vt,Sway,12,.62f);
        Spring(ref closure,ref vclose,eye,down?49:23,1);
        rubSmile=down?Mode=="rub"&&closure>.82f:rubSmile&&closure>.5f;
        var blend=1-MathF.Exp(-dt*14);blush+=(b-blush)*blend;gazeX+=(gx-gazeX)*blend;gazeY+=(gy-gazeY)*blend;
    }
    public Pose Pose(float time){
        float closed=Math.Clamp(closure,0,1);
        return new Pose{HeadX=X,HeadY=Y,HeadAngle=Angle,EyeLeft=1-closed,EyeRight=1-closed,
            SmileEyes=closed,SoftSmileEyes=closed,BrowY=-4*closed,Blush=blush,
            Mouth=rubSmile?"smile":"neutral",MouthMix=rubSmile?1:0,GazeX=gazeX,GazeY=gazeY};
    }
}
