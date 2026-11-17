using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal sealed class MotionController
{
    private readonly Spring hair=new(38,10),tip=new(25,8);
    private readonly FrontHairDynamics frontHair=new();
    private readonly TaskFeedback feedback=new();
    private long feedbackRevision;
    private string reaction="",state="idle";
    private double reactionEnd,lastInteraction,lastReaction=-60,local;
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
    private static double PlaybackRate(string id)=>id=="wink"?3:id=="menu"?Motions.Duration/MenuSeconds:1;
    internal void Touch()=>lastInteraction=Clock;
    internal void ObserveTasks(TaskActivity activity,DateTimeOffset? now=null)
    {if(feedback.Observe(activity,now??DateTimeOffset.UtcNow)){reaction="";Touch();}}
    internal void LookAtMenu(Vector direction)
    {
        menuGaze=new(Math.Clamp(direction.X,-.75,.75),Math.Clamp(direction.Y,-1,1));
        previous=current;transition=0;local=0;reaction="menu";reactionEnd=Clock+MenuSeconds;lastReaction=Clock;Touch();
    }
    internal void React(string id)
    {
        if(Clock-lastReaction<1||id==reaction)return;
        reaction=id;reactionEnd=Clock+Motions.Duration/PlaybackRate(id);lastReaction=Clock;Touch();
    }
    internal Pose Step(double delta,Vector gaze,bool wind,DateTimeOffset? now=null)
    {
        delta=Math.Clamp(delta,0,.08);Clock+=delta;
        if(Clock>=reactionEnd)reaction="";
        var moment=now??DateTimeOffset.UtcNow;var cue=feedback.StateAt(moment);
        var next=reaction!=""?reaction:cue!=""?cue:Clock-lastInteraction>90?"sleepy":"idle";
        if(next!=state||reaction==""&&feedbackRevision!=feedback.Revision){previous=current;transition=0;local=0;state=next;}
        feedbackRevision=feedback.Revision;
        local+=delta;transition+=delta;
        var rate=PlaybackRate(state);var motionTime=reaction==""&&cue!=""?feedback.MotionTimeAt(moment):local*rate%Motions.Duration;
        var pose=MotionLayers.Evaluate(state,motionTime,Clock,wind);
        // The greeting accelerates its scripted wink, not the other eye's ordinary blink cadence.
        if(state=="wink")pose=pose with{EyeLeft=Motions.Evaluate("idle",local%Motions.Duration).EyeLeft};
        if(state=="menu")
        {var blink=Motions.Evaluate("idle",local%Motions.Duration);pose=pose with{EyeLeft=blink.EyeLeft,EyeRight=blink.EyeRight,GazeX=(float)menuGaze.X,GazeY=(float)menuGaze.Y};}
        if(reaction==""&&cue!="")
        {var blink=Motions.Evaluate("idle",local%Motions.Duration);pose=pose with{EyeLeft=blink.EyeLeft,EyeRight=blink.EyeRight};}
        // Sustained states keep real cursor gaze. Only deliberate, short look-away reactions override it.
        var authored=state is "shy" or "annoyed" or "menu"?Motions.Envelope(motionTime/Motions.Duration):0;
        if(state=="menu")pose=pose with{EyeRoll=Math.Clamp(-(float)menuGaze.Y,0,1)*authored};
        pose=pose with{GazeX=(float)gaze.X*(1-authored)+pose.GazeX*authored,
            GazeY=(float)gaze.Y*(1-authored)+pose.GazeY*authored,
            HeadX=pose.HeadX+(float)gaze.X*2*(1-authored),HeadAngle=pose.HeadAngle+(float)gaze.X*.45f*(1-authored)};
        current=Blend(previous,pose,Smooth((float)(transition*rate/.28)));
        // Pat eyelids have their own short, smooth keys; the head still uses its original six-second blend.
        if(state=="pat")current=current with{EyeLeft=pose.EyeLeft,EyeRight=pose.EyeRight,SmileEyes=pose.SmileEyes,SoftSmileEyes=pose.SoftSmileEyes};
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
        Renderer.Draw(canvas!,pose,hair,tip,scale*RenderScale,insetX/scale+6,insetY/scale+10,front:Controller.FrontHair);
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
