namespace MotionLab;

internal sealed record Motion(string Id,string Label,string Trigger,string Notes);
internal sealed record Pose
{
    public string CelId {get;init;}="";public double CelTime {get;init;}
    public float HeadX {get;init;}public float HeadY {get;init;}public float HeadAngle {get;init;}
    public float GazeX {get;init;}public float GazeY {get;init;}
    public float EyeRoll {get;init;}
    public float EyeLeft {get;init;}=1;public float EyeRight {get;init;}=1;
    public float SmileEyes {get;init;}public float SoftSmileEyes {get;init;}public float BrowY {get;init;}public float BrowLeft {get;init;}public float BrowRight {get;init;}
    public float Blush {get;init;}public float Breath {get;init;}public float Wind {get;init;}public float AmbientWind {get;init;}
    public string Mouth {get;init;}="neutral";public float MouthMix {get;init;}public float MouthOpen {get;init;}=1;
}

internal static class Motions
{
    internal const int Fps=30,Frames=180;
    internal const double Duration=6;
    internal static readonly Motion[] Catalog=[
        new("idle","安静待机","idle","Low-amplitude breath; two separately timed blinks; pinned paws."),
        new("focus","专注跟随","working","Eyes lead a subtle head follow; accessories lag."),
        new("breeze","发丝微风","ambient","Independent fringe/side/tip chains; no whole-image wobble."),
        new("happy","开心微笑","success","Closed smiling eyes, warm mouth and restrained head bounce."),
        new("surprise","轻微惊讶","notification","Raised brows, wider simplified orange eyes, small O mouth."),
        new("thinking","歪头思考","thinking","Asymmetric brows, upper-side gaze, small tilt."),
        new("shy","害羞回避","shy","Side/down gaze, small smile and blush; no white eye shapes."),
        new("annoyed","无语不满","warning","Drooped gaze and subtle inward brow angle, not a new face style."),
        new("sleepy","困倦点头","long-idle","Slow lid closure and nod; no constant jitter."),
        new("nod","点头回应","confirm","Two soft nods with hair settling and a return to neutral."),
        new("shake","摇头回应","decline","Small lateral head motion, independently delayed hair/ribbon tips."),
        new("talk","简化说话","speaking-preview","A/I/U mouth variants; illustrative loop, not connected to audio."),
        new("wink","眨单眼","greeting","Viewer-right wink, smile and a restrained tilt."),
        new("pat","摸头反应","head-touch","Smiling closure and soft lean; no touch listener installed."),
        new("complete","任务完成","task-stop","One soft nod, a small smile and a short check bubble; never loops."),
        new("menu","看向菜单","panel-open","A short neutral glance toward the actual menu; no smile or blush.")];
    private static float S(double t,double frequency=1)=>MathF.Sin((float)(2*Math.PI*t*frequency));
    private static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    internal static float Envelope(double t)=>Smooth((float)t/.18f)*(1-Smooth(((float)t-.72f)/.28f));
    private static float Blink(double seconds,double at)
    {
        var t=seconds-at;if(t<0||t>.27)return 0;
        if(t<.08)return Smooth((float)t/.08f);if(t<.12)return 1;return 1-Smooth((float)(t-.12)/.15f);
    }
    internal static Pose Evaluate(string id,double time)
    {
        var t=time/Duration;var e=Envelope(t);var blink=Math.Max(Blink(time,1.15),Blink(time,4.7));
        var p=new Pose{HeadY=-2*S(t),HeadAngle=.2f*S(t),EyeLeft=1-blink,EyeRight=1-blink,Breath=.003f*S(t)};
        return id switch
        {
            "idle"=>p,
            "focus"=>p with{GazeX=.85f*S(t),GazeY=.45f*S(t,2),HeadAngle=.65f*S(t),HeadX=2*S(t)},
            "breeze"=>p with{Wind=4*S(t)+.7f*S(t,2)},
            "happy"=>p with{HeadY=p.HeadY-9*e*(1+S(t,3))*.5f,HeadAngle=1.3f*e*S(t,2),EyeLeft=(1-e)*(1-blink),EyeRight=(1-e)*(1-blink),SmileEyes=e,Mouth="happy",MouthMix=e,Blush=.3f*e},
            "surprise"=>p with{HeadY=-8*e,BrowY=-24*e,EyeLeft=1+.35f*e-blink*(1-e),EyeRight=1+.35f*e-blink*(1-e),Mouth="surprise",MouthMix=e},
            "thinking"=>p with{HeadAngle=-3*e,GazeX=-.8f*e,GazeY=-.45f*e,BrowLeft=-7*e,BrowRight=3*e,BrowY=-3*e},
            "shy"=>p with{HeadAngle=2*e,GazeX=.8f*e,GazeY=.55f*e,Blush=.85f*e,Mouth="smile",MouthMix=e},
            "annoyed"=>p with{BrowLeft=7*e,BrowRight=-7*e,BrowY=5*e,EyeLeft=(1-.35f*e)*(1-blink),EyeRight=(1-.35f*e)*(1-blink),GazeX=-.65f*e,Mouth="i",MouthMix=.65f*e},
            "sleepy"=>p with{EyeLeft=(1-.86f*e)*(1-blink),EyeRight=(1-.86f*e)*(1-blink),HeadY=e*(5+5*S(t,2)),HeadAngle=1.5f*e,Mouth="u",MouthMix=.4f*e},
            "nod"=>p with{HeadY=p.HeadY+11*e*(1-MathF.Cos((float)t*4*MathF.PI))*.5f,GazeY=.25f*e,Mouth="smile",MouthMix=.45f*e},
            "shake"=>p with{HeadX=15*e*S(t,3),HeadAngle=2.3f*e*S(t,3),GazeX=-.2f*e*S(t,3)},
            "talk"=>p with{Mouth=((int)(time*5)%3) switch{0=>"a",1=>"i",_=>"u"},MouthMix=e,MouthOpen=.45f+.55f*MathF.Abs(S(t,8)),HeadAngle=.5f*e*S(t,2)},
            "wink"=>p with{EyeLeft=1,EyeRight=1-e,HeadAngle=-1.8f*e,Mouth="smile",MouthMix=e},
            "complete"=>p with{HeadY=p.HeadY+7*e*(1-MathF.Cos((float)t*2*MathF.PI))*.5f,HeadAngle=.6f*e*S(t),Mouth="smile",MouthMix=.8f*e,Blush=.12f*e},
            "pat"=>PatEyes.Apply(p with{HeadY=4*e,HeadAngle=-2.5f*e,Blush=.4f*e,Wind=1.6f*e*S(t,2),Mouth="smile",MouthMix=e},time,blink),
            "menu"=>p with{GazeX=-.25f,GazeY=-.95f,EyeRoll=1},
            _=>throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
}
