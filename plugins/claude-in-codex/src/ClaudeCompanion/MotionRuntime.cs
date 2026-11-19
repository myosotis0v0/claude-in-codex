using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal sealed class MotionController(bool expressive=true)
{
    private readonly Spring hair=new(38,10),tip=new(25,8);
    private readonly FrontHairDynamics frontHair=new();
    private readonly TaskFeedback feedback=new();
    private readonly TaskBubbles bubbles=new();
    internal IReadOnlyList<TaskBubbleFrame> Bubbles(DateTimeOffset now)=>bubbles.Frames(now);
    private string reaction="",state="idle";
    private double lastInteraction,local,stateStart;
    private sealed record Request(string Id,int Priority,DateTimeOffset Deadline,double ClockDeadline,Vector Direction);
    private readonly List<Request> waiting=[];
    private const int QueueLimit=4;
    private Pose previous=new(),current=new();
    private double transition;
    private Vector menuGaze=new(-.25,-.95);
    internal const double MenuSeconds=1.4;
    internal double Clock {get;private set;}
    internal string State=>state;
    internal Pose Current=>current;
    internal float Hair=>hair.Position;
    internal float Tip=>tip.Position;
    internal FrontHairPose FrontHair=>frontHair.Current;
    internal HeadTouch HeadTouch {get;private set;}=new();
    internal void CancelTouch(){HeadTouch=new();Touch();}
    private static double PlaybackRate(string id)=>id switch{"wink" or "complete-smile"=>3,"complete"=>Motions.Duration/Renderer.PlacardSeconds,"menu"=>Motions.Duration/MenuSeconds,_=>1};
    private void Expire(DateTimeOffset now)=>waiting.RemoveAll(r=>now>=r.Deadline||Clock>=r.ClockDeadline);
    private void Enqueue(string id,DateTimeOffset now,Vector direction=default)
    {
        Expire(now);
        if(id==reaction&&id!="complete"){if(id=="menu")menuGaze=direction;return;}
        var (priority,ttl)=id switch{"wink"=>(5,6.25),"pat"=>(4,6.25),"menu"=>(3,6.25),"complete"=>(2,7d),_=>(1,2d)};
        var req=new Request(id,priority,now.AddSeconds(ttl),Clock+ttl,direction);
        var same=waiting.FindIndex(r=>r.Id==id);
        if(same>=0){waiting[same]=req;return;}
        if(waiting.Count>=QueueLimit){
            var least=waiting.MinBy(r=>r.Priority)!;
            if(least.Priority>=priority)return;
            waiting.Remove(least);
        }
        waiting.Add(req);
    }
    internal void CancelQueuedMenu()=>waiting.RemoveAll(r=>r.Id=="menu");
    internal void Touch()=>lastInteraction=Clock;
    internal void ObserveTasks(TaskActivity activity,DateTimeOffset? now=null)
    {
        var moment=now??DateTimeOffset.UtcNow;bubbles.Observe(activity,moment);
        var revision=feedback.CompleteRevision;
        if(feedback.Observe(activity,moment))Touch();
        if(revision!=feedback.CompleteRevision)Enqueue("complete",new DateTimeOffset(activity.CompletedTicks,TimeSpan.Zero));
    }
    internal void LookAtMenu(Vector direction)
    {
        Enqueue("menu",DateTimeOffset.UtcNow,new(Math.Clamp(direction.X,-.75,.75),Math.Clamp(direction.Y,-1,1)));Touch();
    }
    internal void React(string id)
    {
        Enqueue(id,DateTimeOffset.UtcNow);Touch();
    }
    internal Pose Step(double delta,Vector gaze,bool wind,DateTimeOffset? now=null)
    {
        delta=Math.Clamp(delta,0,.08);Clock+=delta;
        if(reaction!=""&&local+1e-8>=Motions.Duration/PlaybackRate(reaction))reaction="";
        var moment=now??DateTimeOffset.UtcNow;Expire(moment);var started=false;
        HeadTouch.Step((float)Clock,delta);
        if(reaction is ("" or "menu")&&HeadTouch.Playing((float)Clock)&&!waiting.Any(r=>r.Id=="wink")){
            reaction="";CancelQueuedMenu();state="head-touch";Touch();return current=HeadTouch.Pose((float)Clock);
        }
        // Finish the authored phrase of a looping expression, not the entire indefinite state.
        var phase=local%Motions.Duration;
        var finishPhrase=reaction==""&&(state switch{"thinking"=>phase is >=1.1 and <4.65,"sleepy"=>phase is >=.4 and <4.95,_=>false});
        if(state=="thinking"&&waiting.Any(r=>r.Id=="menu"))finishPhrase=false;
        if(reaction==""&&!finishPhrase&&waiting.Count>0){
            var request=waiting.MaxBy(r=>r.Priority)!;waiting.Remove(request);
            reaction=request.Id;if(reaction=="menu")menuGaze=request.Direction;started=true;
        }
        var cue=feedback.Thinking?"thinking":"";
        var next=reaction!=""?reaction:finishPhrase?state:cue!=""?cue:Clock-lastInteraction>90?"sleepy":"idle";
        if(started||next!=state){previous=current;transition=0;local=next=="idle"&&state=="menu"?(Clock-delta)%Motions.Duration:0;state=next;stateStart=Clock;}
        local+=delta;transition+=delta;
        var rate=PlaybackRate(state);var motionTime=reaction!=""?Math.Min(local*rate,Motions.Duration):local%Motions.Duration;
        var menu=state=="menu"&&expressive;var t=Clock-stateStart;
        var pose=menu||state=="complete"?MotionLayers.Evaluate("idle",Clock%Motions.Duration,Clock,wind):MotionLayers.Evaluate(state,motionTime,Clock,wind);
        if(state=="menu")
        {pose=pose with{GazeX=(float)menuGaze.X,GazeY=(float)menuGaze.Y};}
        if(state=="thinking")
        {var blink=Motions.Evaluate("idle",local%Motions.Duration);pose=pose with{EyeLeft=blink.EyeLeft,EyeRight=blink.EyeRight};}
        // Sustained states keep real cursor gaze. Only deliberate, short look-away reactions override it.
        var authored=menu?Acting.Beat(t,0,.19,.80,1.28):state is "shy" or "annoyed" or "menu"?Motions.Envelope(motionTime/Motions.Duration):0;
        if(state=="menu")pose=pose with{EyeRoll=Math.Clamp(-(float)menuGaze.Y,0,1)*authored};
        pose=pose with{GazeX=(float)gaze.X*(1-authored)+pose.GazeX*authored,
            GazeY=(float)gaze.Y*(1-authored)+pose.GazeY*authored,
            HeadX=pose.HeadX+(float)gaze.X*2*(menu?1:1-authored),HeadAngle=pose.HeadAngle+(float)gaze.X*.45f*(menu?1:1-authored)};
        current=Blend(previous,pose,Smooth((float)(transition*rate/.28)));
        // Do not inherit an automatic blink from either the wink clip or its entry blend.
        if(state=="wink")current=current with{EyeLeft=1};
        // Pat eyelids have their own short, smooth keys; the head still uses its original six-second blend.
        if(state=="pat")current=current with{EyeLeft=pose.EyeLeft,EyeRight=pose.EyeRight,SmileEyes=pose.SmileEyes,SoftSmileEyes=pose.SoftSmileEyes};
        if(expressive)
        {
            if(menu)current=current with{MenuPitchWeight=Acting.Beat(t,2d/30,.36,.80,1.28)*Math.Clamp((float)-menuGaze.Y/.5f,0,1)};
            var seconds=reaction!=""?local:motionTime;
            current=Acting.Apply(state,current,seconds);
        }
        var steps=Math.Max(1,(int)Math.Ceiling(delta*120));var dt=(float)(delta/steps);
        for(var i=0;i<steps;i++)
        {
            var force=current.Wind+current.AmbientWind-current.HeadAngle*.85f-current.HeadX*.05f;
            var root=hair.Step(force,dt);tip.Step(root,dt);frontHair.Step(force,dt);
        }
        return current;
    }
    private static float Smooth(float v){v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
    private static Pose Blend(Pose a,Pose b,float w)
    {
        float L(float x,float y)=>x+(y-x)*w;
        return new Pose{HeadX=L(a.HeadX,b.HeadX),HeadY=L(a.HeadY,b.HeadY),HeadAngle=L(a.HeadAngle,b.HeadAngle),
            GazeX=L(a.GazeX,b.GazeX),GazeY=L(a.GazeY,b.GazeY),EyeLeft=L(a.EyeLeft,b.EyeLeft),EyeRight=L(a.EyeRight,b.EyeRight),
            EyeRoll=L(a.EyeRoll,b.EyeRoll),
            SmileEyes=L(a.SmileEyes,b.SmileEyes),SoftSmileEyes=L(a.SoftSmileEyes,b.SoftSmileEyes),BrowY=L(a.BrowY,b.BrowY),BrowLeft=L(a.BrowLeft,b.BrowLeft),BrowRight=L(a.BrowRight,b.BrowRight),
            Blush=L(a.Blush,b.Blush),Breath=L(a.Breath,b.Breath),Wind=L(a.Wind,b.Wind),AmbientWind=L(a.AmbientWind,b.AmbientWind),
            Mouth=b.Mouth,MouthMix=L(a.Mouth==b.Mouth?a.MouthMix:0,b.MouthMix),MouthOpen=L(a.MouthOpen,b.MouthOpen)};
    }
}

internal sealed class MotionSurface:IDisposable
{
    internal readonly MotionController Controller=new();
    internal readonly Renderer Renderer;
    private SKBitmap? surface;
    private SKBitmap? working;
    private SKCanvas? canvas;
    private SKCanvas? resolveCanvas;
    private readonly List<(SKBitmap Bitmap,SKCanvas Canvas)> resolveStages=[];
    private WriteableBitmap? frame;
    internal int RenderScale {get;}
    internal long BufferBytes=>(long)(surface?.ByteCount??0)+(working?.ByteCount??0)+resolveStages.Sum(p=>(long)p.Bitmap.ByteCount);
    internal MotionSurface(int renderScale=2,bool highQuality=true)
    {
        if(renderScale is not(1 or 2 or 4 or 8))throw new ArgumentOutOfRangeException(nameof(renderScale));
        RenderScale=renderScale;
        Renderer=new Renderer(Path.Combine(AppContext.BaseDirectory,"assets","motion"),highQuality);
    }
    internal WriteableBitmap Draw(int width,int height,float artworkPixels,float insetX,float insetY)
        =>DrawState(width,height,artworkPixels,insetX,insetY,Controller.Current,Controller.Hair,Controller.Tip);
    internal WriteableBitmap DrawState(int width,int height,float artworkPixels,float insetX,float insetY,Pose pose,float hair,float tip)
    {
        width=Math.Max(1,width);height=Math.Max(1,height);
        if(surface?.Width!=width||surface.Height!=height)
        {
            canvas?.Dispose();resolveCanvas?.Dispose();working?.Dispose();surface?.Dispose();
            DisposeResolveStages();
            surface=new SKBitmap(width,height,SKColorType.Bgra8888,SKAlphaType.Premul);
            working=new SKBitmap(width*RenderScale,height*RenderScale,SKColorType.Bgra8888,SKAlphaType.Premul);
            canvas=new SKCanvas(working);resolveCanvas=new SKCanvas(surface);
            for(var factor=RenderScale/2;factor>1;factor/=2)
            {
                var intermediate=new SKBitmap(width*factor,height*factor,SKColorType.Bgra8888,SKAlphaType.Premul);
                resolveStages.Add((intermediate,new SKCanvas(intermediate)));
            }
            frame=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
        }
        var scale=artworkPixels/1254;
        // Supersample physical pixels, not DIPs. Resolve before WPF; hit testing stays at display size.
        // Crop at the fingertips and the base of the chest ornament, not the longer rear hair.
        float foregroundBaseline=Renderer.Hands.Enabled?1274:1216;
        var ox=insetX/scale+6;var oy=insetY/scale+(1254-foregroundBaseline);
        if(Controller.State=="head-touch")Renderer.DrawTouch(canvas!,pose,Controller.HeadTouch,scale*RenderScale,ox,oy);
        else Renderer.Draw(canvas!,pose,hair,tip,scale*RenderScale,ox,oy,front:Controller.FrontHair);
        var source=working!;
        // Each 2:1 linear resolve covers all four source samples; repeated stages also make 4x/8x references valid.
        foreach(var stage in resolveStages)
        {
            stage.Canvas.Clear(SKColors.Transparent);
            stage.Canvas.DrawBitmap(source,new SKRect(0,0,stage.Bitmap.Width,stage.Bitmap.Height),new SKSamplingOptions(SKFilterMode.Linear));
            source=stage.Bitmap;
        }
        resolveCanvas!.Clear(SKColors.Transparent);
        resolveCanvas.DrawBitmap(source,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear));
        frame!.WritePixels(new Int32Rect(0,0,width,height),surface.GetPixels(),surface.ByteCount,surface.RowBytes);
        return frame;
    }
    internal bool Hit(double x,double y,double scale)
    {
        if(surface==null)return false;
        var px=(int)(x*scale);var py=(int)(y*scale);
        return px>=0&&py>=0&&px<surface.Width&&py<surface.Height&&surface.GetPixel(px,py).Alpha>32;
    }
    private void DisposeResolveStages(){foreach(var stage in resolveStages){stage.Canvas.Dispose();stage.Bitmap.Dispose();}resolveStages.Clear();}
    public void Dispose(){canvas?.Dispose();resolveCanvas?.Dispose();working?.Dispose();surface?.Dispose();DisposeResolveStages();Renderer.Dispose();}
}
