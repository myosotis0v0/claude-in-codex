using System.Numerics;
namespace MotionLab;

internal sealed class HeadTouch{
    internal const double MultiClick=.38;
    private Vector2 point,lo,hi,lastPoint;
    private bool held;
    private double accumulator;
    internal bool Active {get;private set;}
    internal bool Connected {get;private set;}
    internal void Begin(float time){
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
    internal bool End(float time){
        if(!held)return false;
        bool click=!repeated&&time-pressed<.24f&&Mode=="press";
        wasGesture=!click;held=false;Sample(time,point,false);ended=time;return click;
    }
    private float ended=-10;
    private bool wasGesture;
    internal bool PendingSingle=>!held&&!wasGesture&&!repeated;
    internal bool Playing(float time)=>Active&&(held?(Mode!="press"||repeated||time-pressed>=.24f):!PendingSingle&&time-ended<.55f);
    internal void Step(float time,double delta){
        if(!Active)return;
        if(!held&&time-ended>=.55f){Active=false;Connected=false;return;}
        accumulator+=Math.Clamp(delta,0,.08);
        while(accumulator>=dt){accumulator-=dt;Sample(time-(float)accumulator,point,held);}
    }
    const float dt=1f/120;
    readonly Queue<(float t,Vector2 p)> samples=new();
    public string Mode="press";
    public float X,Y,Angle,Sway,Tip;
    readonly float[] comb=new float[3],speed=new float[3],ends=new float[3],endSpeed=new float[3];
    float vx,vy,va,vs,vt,closure,vclose,blush,gazeX,gazeY;
    bool rubSmile,wasDown,repeated;
    float pressed,lastRelease=-5,pulse=-5,vYaw,vPitch;
    public int Taps;
    public float Yaw,Pitch,Squash,Brush;
    float brushVel;
    public FrontHairPose Front=>new(Sway*.62f+comb[0],Tip*.77f+ends[0],Sway*.33f+comb[1],Tip*.44f+ends[1],Sway*.71f+comb[2],Tip*.85f+ends[2]);
    static void Spring(ref float value,ref float v,float target,float frequency,float damping){
        v+=(frequency*frequency*(target-value)-2*damping*frequency*v)*dt;
        value+=v*dt;
    }
    public void Sample(float t,Vector2 p,bool down){
        float x=0,y=0,angle=0,eye=0,b=0,gx=0,gy=0;
        if(down&&!wasDown){
            pressed=t; repeated=t-lastRelease<MultiClick;
            if(repeated){pulse=t;Taps++;}
        }
        if(!down&&wasDown){
            if(t-pressed<.24f&&Mode=="press"){if(!repeated)Taps++;lastRelease=t;}else lastRelease=-5;
        }
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
        // Bend each rooted front lock locally. A tip follows its own lock, not the face.
        for(int strand=0;strand<3;strand++){
            float distance=p.X-(strand-1)*.62f;
            float influence=.38f+.62f*MathF.Exp(-distance*distance/.64f);
            float brush=down&&Mode=="rub"?-p.X*(strand==1?3.4f:2.8f)*influence:0;
            Spring(ref comb[strand],ref speed[strand],brush,19+strand,.76f);
            Spring(ref ends[strand],ref endSpeed[strand],comb[strand]*.8f,13+strand,.71f);
        }
        Spring(ref Brush,ref brushVel,down&&Mode is ("rub" or "circle")?p.X:0,16,.7f);
        Spring(ref Yaw,ref vYaw,yaw,21,.82f);Spring(ref Pitch,ref vPitch,pitch,21,.82f);
        Spring(ref X,ref vx,x,18,.83f);Spring(ref Y,ref vy,y,17,down?.72f:.50f);
        // A stroke drives the target angle; only the settling motion is time-based.
        Spring(ref Angle,ref va,angle,Mode=="rub"?13:17,Mode=="rub"||!down?.61f:.78f);
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
