using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionLab;

namespace ClaudeCompanion;

// Uses only synthetic poses and offscreen buffers. No window, pointer, account or settings access.
internal static class QualityDiagnostics
{
    internal static void Run()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("Set a workspace output directory.");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
        using var before=new MotionSurface(1,false);
        using var filtered=new MotionSurface(1,true);
        using var after=new MotionSurface();
        using var reference=new MotionSurface(8,true);
        Check(after.RenderScale==2,"production defaults to 2x physical-pixel supersampling");
        var hands=after.Renderer.Hands;
        Check(new Settings().DeskInput&&JsonSerializer.Deserialize<Settings>("{\"DeskInput\":false}")?.DeskInput==false,"desk input defaults on and respects an explicit opt-out");
        Check(hands.MouseX==-14,"mouse starts away from the keyboard");
        hands.Enabled=true;hands.Key(0x1e,false);hands.Cursor(9000);
        hands.Step(1f/30);Check(hands.KeyY< -8,"keyboard hand lifts during travel");
        for(int i=1;i<5;i++)hands.Step(1f/30);
        Check(hands.KeyX< -300&&hands.MouseX is >24 and <=28,"A reaches left; mouse uses expanded bounds");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-left.png");
        hands.Key(0x1e,false,true);hands.Key(0x1c,false);for(int i=0;i<7;i++)hands.Step(1f/30);
        Check(hands.KeyX>90,"Enter reaches the right side before the hold expires");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-right.png");
        for(int i=0;i<90;i++){hands.Key(0x1c,false);hands.Step(1f/30);}
        Check(Math.Abs(hands.KeyX-121)<.01&&Math.Abs(hands.KeyY)<.01,"held key stays planted despite repeat events");
        hands.Key(0x1c,false,true);for(int i=0;i<12;i++)hands.Step(1f/30);
        Check(Math.Abs(hands.KeyX-121)<.01,"release grace period holds position");
        for(int i=0;i<60;i++)hands.Step(1f/30);
        Check(Math.Abs(hands.KeyX)<.01&&Math.Abs(hands.KeyY)<.01&&Math.Abs(hands.MouseX-28)<.01,"keyboard hand returns while the mouse holds its position");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-rest.png");
        hands.Enabled=false;hands.Reset();
        var errors=new List<object>();double oldError=0,newError=0;
        foreach(var size in new[]{160,200,280})
        {
            var pad=(int)Math.Ceiling(size*73d/1254);var n=size+pad;
            for(var i=0;i<5;i++)
            {
                var pose=Motions.Evaluate("thinking",1.8+i*.37) with{HeadX=3.75f*i,HeadAngle=.27f+i*.31f};
                var old=before.DrawState(n,n,size,pad,pad,pose,1.35f,-.65f);
                var one=filtered.DrawState(n,n,size,pad,pad,pose,1.35f,-.65f);
                var improved=after.DrawState(n,n,size,pad,pad,pose,1.35f,-.65f);
                var gold=reference.DrawState(n,n,size,pad,pad,pose,1.35f,-.65f);
                var a=EdgeError(old,gold);var b=EdgeError(improved,gold);oldError+=a;newError+=b;
                errors.Add(new{Pixels=size,Pose=i,OldEdgeMae=a,HqOnlyEdgeMae=EdgeError(one,gold),NewEdgeMae=b});
                Check(improved.PixelWidth==n&&improved.PixelHeight==n,$"display bitmap dimensions unchanged at {size}px pose {i}");
                if(i==2)
                {
                    Diagnostics.Save(old,$"old-{size}.png");Diagnostics.Save(one,$"hq-only-{size}.png");
                    Diagnostics.Save(improved,$"new-{size}.png");Diagnostics.Save(gold,$"reference-{size}.png");
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"edge-errors.json"),JsonSerializer.Serialize(new{Cases=errors,Old=oldError/15,New=newError/15},new JsonSerializerOptions{WriteIndented=true}));
        Check(newError<oldError,"edge error versus area-resolved 8x reference improves across identical poses");
        foreach(var dpi in new[]{1d,1.25,1.5,2})
        {
            var size=(int)(160*dpi);var pad=(int)Math.Ceiling(size*73d/1254);var n=size+pad;
            var image=after.DrawState(n,n,size,pad,pad,new Pose(),0,0);
            Check(after.Hit((pad+size*.5)/dpi,(pad+size*.7)/dpi,dpi)&&!after.Hit(0,0,dpi),$"resolved alpha hit testing unchanged at DPI {dpi}");
            var px=Pixels(image);var bottom=Enumerable.Range(0,n).Max(x=>px[((n-1)*n+x)*4+3]);
            Check(bottom>32,$"bottom anchor remains flush at DPI {dpi}");
            foreach(var span in new[]{(230,405),(842,1010),(550,703)}){
                var chest=span.Item1==550;
                int left=pad+(span.Item1+6)*size/1254,right=pad+(span.Item2+6)*size/1254;
                var count=Enumerable.Range(left,right-left).Count(x=>{
                    var k=((n-1)*n+x)*4;
                    return px[k+3]>220&&(chest?px[k+2]<100:px[k+1]>155&&px[k]>140);
                });
                Check(count>=3,$"foreground band {span} reaches final row at DPI {dpi}");
            }
        }
        var stable=after.Draw(212,212,200,12,12);var bytes=after.BufferBytes;
        Check(ReferenceEquals(stable,after.Draw(212,212,200,12,12))&&bytes==after.BufferBytes,"same-size draws reuse working, resolved and WPF buffers");
        Check(after.BufferBytes==212L*212*4*5,"2x working plus 1x resolve use exactly five pixel buffers worth of native storage");
        Check(after.Draw(180,220,160,10,10).PixelHeight==220,"rectangular/native-edge overscan retains output dimensions");
        VerifyBubble(after,Check);
        foreach(var stage in new[]{"before","after"})Directory.CreateDirectory(Path.Combine(output,stage));
        for(var i=0;i<90;i++)
        {
            var t=i/30d;var pose=Motions.Evaluate("thinking",t+1) with{HeadX=10*(float)Math.Sin(t*2),HeadAngle=.8f*(float)Math.Sin(t*1.7)};
            Diagnostics.Save(before.DrawState(212,212,200,12,12,pose,(float)Math.Sin(t),.5f),Path.Combine("before",$"{i:D3}.png"));
            Diagnostics.Save(after.DrawState(212,212,200,12,12,pose,(float)Math.Sin(t),.5f),Path.Combine("after",$"{i:D3}.png"));
        }
        var timings=new List<object>();
        foreach(var size in new[]{160,200,280,400,720})foreach(var pair in new[]{(Name:"old",Surface:before),(Name:"new",Surface:after)})
        {
            var pad=(int)Math.Ceiling(size*73d/1254);var times=new List<double>();
            for(var i=0;i<32;i++)
            {
                var sw=Stopwatch.StartNew();pair.Surface.DrawState(size+pad,size+pad,size,pad,pad,new Pose{HeadAngle=.8f},1,.5f);sw.Stop();
                if(i>=6)times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();timings.Add(new{Pixels=size,Mode=pair.Name,MedianMs=times[times.Count/2],P95Ms=times[(int)(times.Count*.95)],NativeBufferBytes=pair.Surface.BufferBytes});
        }
        File.WriteAllText(Path.Combine(output,"quality-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Errors=errors,
            AverageEdgeErrorBefore=oldError/15,AverageEdgeErrorAfter=newError/15,Timings=timings,
            Scope="Same source layers, poses, geometry, physical output sizes and alpha compositing. Eight-times supersampled render with repeated 2:1 area resolves is a sampling reference, not an independent ground-truth photograph. Offscreen only; no desktop control or installed-process restart."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var bytes=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return bytes;
    }
    private static double EdgeError(BitmapSource image,BitmapSource reference)
    {
        var a=Pixels(image);var b=Pixels(reference);var width=image.PixelWidth;double sum=0;var count=0;
        double Composite(byte[] pixels,int p,int c)=>pixels[p+c]+(c==0?34:32)*(1-pixels[p+3]/255d);
        for(var y=1;y<image.PixelHeight-1;y++)for(var x=1;x<width-1;x++)
        {
            var p=(y*width+x)*4;var edge=b[p+3]>0&&b[p+3]<255;
            for(var c=0;c<3;c++)edge|=Math.Abs(Composite(b,p,c)-Composite(b,p+4,c))>18||Math.Abs(Composite(b,p,c)-Composite(b,p+width*4,c))>18;
            if(!edge)continue;
            for(var c=0;c<3;c++){sum+=Math.Abs(Composite(a,p,c)-Composite(b,p,c));count++;}
        }
        return sum/Math.Max(1,count);
    }
    private static void VerifyBubble(MotionSurface surface,Action<bool,string> check)
    {
        foreach(var state in new[]{"idle","thinking","complete","pat","shake","happy"})foreach(var mode in new[]{"light","dark"})
        {
            var bg=new SolidColorBrush(mode=="light"?Colors.White:Color.FromRgb(32,32,34));
            var character=surface.DrawState(212,212,200,12,12,Motions.Evaluate(state,2.8),3,2);
            // Deliberately overlap the hair/flower. This is the exact final production composition function.
            var origin=new Point(145,54);var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())PetView.DrawMotionLayers(dc,character,new Rect(0,0,212,212),origin,1,1,bg,state=="complete"?"complete":"thinking");
            var image=new RenderTargetBitmap(212,212,96,96,PixelFormats.Pbgra32);image.Render(visual);
            var pixels=Pixels(image);var p=(59*212+150)*4;var color=bg.Color;
            check(pixels[p]==color.B&&pixels[p+1]==color.G&&pixels[p+2]==color.R&&pixels[p+3]==255,$"bubble paints above overlapping hair/flower: {state}, {mode}");
            if(state=="thinking"&&mode=="dark")Diagnostics.Save(image,"bubble-over-hair.png");
        }
    }
}
