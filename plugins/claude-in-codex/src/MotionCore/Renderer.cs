using SkiaSharp;
using System.Numerics;
using System.IO;

namespace MotionLab;

internal sealed partial class Renderer:IDisposable
{
    private readonly Dictionary<string,TexturePart> images=[];
    internal long TextureBytes=>images.Values.Sum(p=>(long)p.Bitmap.ByteCount);
    internal Rig Rig {get;}=new();
    internal Mesh[] Meshes {get;}=[new("hair"),new("accessory",18,28),new("neck",12,12),new("clothes-paws",24,16),new("rear-hair",24,18)];
    internal Mesh[] FrontMeshes {get;}=[new("hair-front-left",40,44),new("hair-front-center",32,40),new("hair-front-right",40,44),new("hair-backing",24,28)];
    internal bool HasFrontHair=>images.ContainsKey("hair-front-left");
    internal Mesh? RearCurtain {get;}
    private readonly SKPaint paint=new(){IsAntialias=true};
    private readonly SKSamplingOptions sampling;
    private readonly bool highQuality;
    private static readonly SKColor Ink=new(43,25,30);
    internal Renderer(string root,bool highQuality=true,bool rearOverscan=true)
    {
        var props=Path.Combine(root,"..","peripherals");
        if(File.Exists(Path.Combine(props,"mouse.png"))&&File.Exists(Path.Combine(props,"keyboard.png")))deskRoot=props;
        this.highQuality=highQuality;sampling=highQuality?new(SKCubicResampler.Mitchell):new(SKFilterMode.Linear);
        var split=File.Exists(Path.Combine(root,"layers","hair-front-left.png"));
        foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"layers"),"*.png"))
        {
            if(split&&Path.GetFileName(file)=="hair.png")continue;
            images[Path.GetFileNameWithoutExtension(file)]=new TexturePart(file);
        }
        if(images.ContainsKey("hair-curtain"))
        {
            RearCurtain=new Mesh("hair-curtain",32,32,rearOverscan){RearFlow=true};
            Meshes[4].Dispose();Meshes[4]=new Mesh("rear-hair",24,18,rearOverscan);
            FrontMeshes[3].Dispose();FrontMeshes[3]=new Mesh("hair-backing",24,28,rearOverscan);
            Meshes[4].RearFlow=true;FrontMeshes[3].RearFlow=true;
        }
    }
    internal SKBitmap Render(Pose pose,float sway,float tip,int size=336,bool debug=false,FrontHairPose front=default)
    {
        var bitmap=new SKBitmap(size,size,SKColorType.Bgra8888,SKAlphaType.Premul);using var canvas=new SKCanvas(bitmap);
        Draw(canvas,pose,sway,tip,size/1400f,73,73,debug,front);return bitmap;
    }
    internal void Draw(SKCanvas canvas,Pose pose,float sway,float tip,float scale,float x,float y,bool debug=false,FrontHairPose front=default)
    {
        Rig.Update(pose,sway,tip,front);canvas.Clear(SKColors.Transparent);
        if(pose.MenuPitchWeight>0&&HasFrontHair){DrawMenuFrame(canvas,pose,scale,x,y,debug);return;}
        canvas.Save();
        canvas.Scale(scale);canvas.Translate(x,y);
        DrawHead(canvas,pose);
        DrawForeground(canvas);
        if(debug)DrawDebug(canvas);
        canvas.Restore();
    }
    private void DrawHead(SKCanvas canvas,Pose pose)
    {
        DrawMesh(canvas,4,"rear-hair");DrawMesh(canvas,2,"neck");
        if(HasFrontHair)DrawFront(canvas,3,"hair-backing");
        if(RearCurtain!=null)
        DrawRear(canvas,RearCurtain,"hair-curtain");
        var lift=pose.MenuPitchWeight;
        if(lift>0)DrawMenuNeck(canvas,lift);
        if(images.ContainsKey("cape-shoulders")){
            DrawPart(canvas,"cape-shoulders",paint);
            DrawMesh(canvas,3,"chest-ornament");
        }
        canvas.Save();canvas.Concat(Rig.ToSkia(Rig.Skin[1]));
        if(lift>0)canvas.Translate(0,-36*.19f*lift);
        DrawPart(canvas,"face",paint);
        if(lift>0)canvas.Translate(0,-(164-36)*.19f*lift);
        DrawEye(canvas,true,pose.EyeLeft,pose);DrawEye(canvas,false,pose.EyeRight,pose);
        DrawBrows(canvas,pose);
        if(lift>0)canvas.Translate(0,-(187-164)*.19f*lift);
        DrawMouth(canvas,pose);
        if(pose.Blush>.001)
        {
            using var blush=new SKPaint{IsAntialias=true,Color=new SKColor(243,118,128,(byte)(75*pose.Blush))};
            canvas.DrawOval(new SKRect(337,861,437,900),blush);canvas.DrawOval(new SKRect(810,861,910,900),blush);
        }
        DrawCels(canvas,pose);
        canvas.Restore();
        if(HasFrontHair)
        {
            DrawFront(canvas,0,"hair-front-left");DrawFront(canvas,2,"hair-front-right");DrawFront(canvas,1,"hair-front-center");
            canvas.Save();canvas.Concat(Rig.ToSkia(Rig.Skin[1]));DrawPart(canvas,"hair-cap",paint);canvas.Restore();
        }
        else DrawMesh(canvas,0,"hair");
        DrawMesh(canvas,1,"accessory");
    }
    private void DrawMesh(SKCanvas c,int index,string name)=>DrawRear(c,Meshes[index],name);
    private void DrawFront(SKCanvas c,int index,string name)=>DrawRear(c,FrontMeshes[index],name);
    private void DrawRear(SKCanvas c,Mesh mesh,string name)
    {
        var p=images[name];
        if(mesh.BottomOverscan)
        {
            // This cut belongs to the viewport, not the moving head bone.
            c.Save();var clip=c.LocalClipBounds;clip.Bottom=Math.Min(clip.Bottom,RearHairFlow.DisplayBottom);c.ClipRect(clip,SKClipOperation.Intersect,false);
        }
        mesh.Draw(c,Rig,p.Bitmap,p.X,p.Y,highQuality);
        if(mesh.BottomOverscan)c.Restore();
    }
    private void DrawPart(SKCanvas c,string name,SKPaint ink){var p=images[name];c.DrawBitmap(p.Bitmap,p.X,p.Y,sampling,ink);}
    private void DrawEye(SKCanvas c,bool left,float openness,Pose pose)
    {
        openness=Math.Clamp(openness,0,1.35f);var center=left?414f:846f;var start=left?279f:713f;var end=left?548f:978f;
        var close=Math.Max(0,1-openness)*60;var thickness=.25f+.75f*Math.Min(1.35f,openness);
        float Y(float x,float y)
        {
            var bow=MathF.Pow(MathF.Sin(MathF.PI*Math.Clamp((x-start)/(end-start),0,1)),2);
            var smileLift=pose.SoftSmileEyes*Math.Clamp((1-openness)/.75f,0,1);
            return 757+(y-757)*thickness+close*(.7f+.3f*bow)+pose.GazeY*(pose.GazeY<0?8:12)*bow+32*smileLift+(18*pose.SmileEyes-50*smileLift)*bow;
        }
        if(openness>.03)
        {
            var inward=left?pose.GazeX>0:pose.GazeX<0;
            var dx=pose.GazeX*(inward?18:20);var origin=left?388:728;
            var lip=Y(origin+74,789);var eyeHeight=244*(.45f+.55f*openness);var bottom=lip+110*openness;
            c.Save();c.ClipRect(new SKRect(left?368:703,lip,left?563:901,Math.Max(lip,bottom+1)));
            var eye=images["eye"];
            // A deliberate upward roll moves the iris behind the lash, independently of normal lid/gaze motion.
            var roll=50*Math.Clamp(pose.EyeRoll,0,1);
            c.DrawBitmap(eye.Bitmap,new SKRect(origin+dx+eye.X, bottom-eyeHeight+eye.Y*eyeHeight/244-roll,
                origin+dx+eye.X+eye.Bitmap.Width,bottom-eyeHeight+(eye.Y+eye.Bitmap.Height)*eyeHeight/244-roll),sampling,paint);c.Restore();
        }
        using var path=new SKPathBuilder();
        void Move(float x,float y)=>path.MoveTo(x,Y(x,y));
        void Quad(float x1,float y1,float x2,float y2)=>path.QuadTo(x1,Y(x1,y1),x2,Y(x2,y2));
        void Cubic(float x1,float y1,float x2,float y2,float x3,float y3)=>path.CubicTo(x1,Y(x1,y1),x2,Y(x2,y2),x3,Y(x3,y3));
        if(left)
        {
            Move(279,746);Quad(313,760,345,758);Cubic(395,758,493,756,532,758);
            Quad(548,760,547,772);Quad(550,789,536,790);Cubic(470,789,356,789,328,790);Quad(303,787,279,746);
        }
        else
        {
            Move(978,745);Quad(946,759,925,758);Cubic(860,758,775,757,728,758);
            Quad(708,758,716,772);Quad(708,788,723,790);Cubic(780,789,900,790,930,789);Quad(955,782,978,745);
        }
        path.Close();using var shape=path.Detach();using var ink=new SKPaint{IsAntialias=true,Color=Ink};c.DrawPath(shape,ink);
        using var crease=new SKPaint{IsAntialias=true,Color=new SKColor(158,72,54,(byte)(160*Math.Min(1,openness))),Style=SKPaintStyle.Stroke,StrokeWidth=2.5f,StrokeCap=SKStrokeCap.Round};
        using var line=new SKPathBuilder();line.MoveTo(center-45,733+close*.4f);line.QuadTo(center,731+close*.4f,center+45,735+close*.4f);using var creasePath=line.Detach();c.DrawPath(creasePath,crease);
    }
    private void DrawBrows(SKCanvas c,Pose p)
    {
        foreach(var left in new[]{true,false})
        {
            var origin=new Vector2(left?435:805,662);var angle=(left?p.BrowLeft:p.BrowRight)*MathF.PI/180;
            var m=Matrix3x2.CreateTranslation(-origin)*Matrix3x2.CreateRotation(angle)*Matrix3x2.CreateTranslation(origin+new Vector2(0,p.BrowY));
            c.Save();c.Concat(Rig.ToSkia(m));DrawPart(c,left?"brow-left":"brow-right",paint);c.Restore();
        }
    }
    private void DrawMouth(SKCanvas c,Pose p)
    {
        if(p.Mouth=="yawn")
        {
            // One opaque silhouette changes shape. Never draw the neutral sprite under a yawn.
            var w=Math.Clamp(p.MouthOpen,0,1);
            if(w<=.001f){DrawPart(c,"mouth-neutral",paint);return;}
            var hw=32-13*w;var cy=967-4*w;var top=cy-15-13*w;var bottom=top+7+47*w;
            var flat=10*(1-w);
            using var builder=new SKPathBuilder();builder.MoveTo(632-hw,cy);
            builder.CubicTo(632-hw+flat,top,632+hw-flat,top,632+hw,cy);
            builder.CubicTo(632+hw-flat,bottom,632-hw+flat,bottom,632-hw,cy);builder.Close();
            using var contour=builder.Detach();using var color=new SKPaint{IsAntialias=true,Color=new SKColor(139,47,42)};
            c.DrawPath(contour,color);return;
        }
        using var neutral=new SKPaint{IsAntialias=true,Color=SKColors.White.WithAlpha((byte)(255*(1-p.MouthMix)))};
        DrawPart(c,"mouth-neutral",neutral);
        if(p.MouthMix<=.001||p.Mouth=="neutral")return;
        var size=p.Mouth switch{"smile"=>(60f,20f),"happy"=>(68f,42f),"surprise"=>(36f,43f),"a"=>(58f,43f),"i"=>(58f,13f),_=>(32f,41f)};
        var height=size.Item2*(p.Mouth is "a" or "i" or "u"?p.MouthOpen:1);
        using var fill=new SKPaint{IsAntialias=true,Color=SKColors.White.WithAlpha((byte)(p.MouthMix*255))};
        var mouth=images["mouth-"+p.Mouth];
        c.DrawBitmap(mouth.Bitmap,new SKRect(632-size.Item1/2,962-height/2,632+size.Item1/2,962+height/2),sampling,fill);
    }
    private void DrawDebug(SKCanvas c)
    {
        using var line=new SKPaint{IsAntialias=true,Color=new SKColor(60,225,190),StrokeWidth=5,Style=SKPaintStyle.Stroke};
        foreach(var (bone,index) in Rig.Bones.Select((b,i)=>(b,i)).Skip(1))
        {
            var world=Vector2.Transform(bone.Rest,Rig.Skin[index]);var parent=Rig.Bones[bone.Parent];var previous=Vector2.Transform(parent.Rest,Rig.Skin[bone.Parent]);
            if(bone.Parent>0)c.DrawLine(previous.X,previous.Y,world.X,world.Y,line);
            c.DrawCircle(world.X,world.Y,9,line);
        }
    }
    public void Dispose(){mouseHand.Dispose();keyHand.Dispose();deskMouse?.Dispose();deskKeyboard?.Dispose();menuCanvas?.Dispose();menuBuffer?.Dispose();foreach(var path in celPaths.Values)path.Dispose();foreach(var part in images.Values)part.Bitmap.Dispose();foreach(var mesh in Meshes.Concat(FrontMeshes))mesh.Dispose();RearCurtain?.Dispose();paint.Dispose();}
}

internal sealed class TexturePart
{
    internal SKBitmap Bitmap {get;}
    internal int X {get;}internal int Y {get;}
    internal TexturePart(string path)
    {
        using var source=SKBitmap.Decode(path)??throw new InvalidDataException(path);
        var left=source.Width;var top=source.Height;var right=0;var bottom=0;
        for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++)if(source.GetPixel(x,y).Alpha>0)
        {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
        // Transparent sampling guards stop clamp shaders smearing a cropped edge.
        X=Math.Max(0,left-2);Y=Math.Max(0,top-2);right=Math.Min(source.Width,Math.Max(X+1,right+2));bottom=Math.Min(source.Height,Math.Max(Y+1,bottom+2));
        Bitmap=new SKBitmap(right-X,bottom-Y,SKColorType.Bgra8888,SKAlphaType.Premul);
        using var canvas=new SKCanvas(Bitmap);canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(source,-X,-Y,new SKSamplingOptions(SKFilterMode.Nearest));
    }
}
