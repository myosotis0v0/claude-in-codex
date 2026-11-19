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
        hands.Enabled=true;
        void Frames(int n){for(int i=0;i<n;i++){
            hands.Step(1f/60);
            var (left,right)=hands.DrawOffsets();
            if(DeskHands.KeyboardCenter+right-DeskHands.PointerCenter-left<DeskHands.MinimumGap-.001f)
                throw new InvalidOperationException("Overlapping paw centers during movement");
        }}
        var kb=hands.Keyboard;var pointer=hands.Pointer;
        byte[] MouseFrame()=>Pixels(after.DrawState(192,192,192,0,0,new Pose(),0,0));
        var rest=MouseFrame();
        foreach(var (mask,delta,expected) in new[]{(1,0,FingerPose.Index),(4,0,FingerPose.Little),
            (0x400,120,FingerPose.Middle),(0x400,-120,FingerPose.Middle),(0x405,120,FingerPose.Index|FingerPose.Middle|FingerPose.Little)}){
            hands.MouseButtons(mask,delta);Frames(1);
            var frame=MouseFrame();var changed=Enumerable.Range(0,192*192)
                .Where(i=>!frame.AsSpan(i*4,4).SequenceEqual(rest.AsSpan(i*4,4))).ToArray();
            Check(hands.MousePose==expected&&changed.Length>0&&changed.All(i=>i%192 is >20 and <60&&i/192>168),
                $"192px mouse pose changes only fingertips: {mask}/{delta}");
            Frames(30);Check(hands.MousePose==(expected&~FingerPose.Middle),"held buttons persist, wheel releases");
            hands.MouseButtons(10,0);Frames(1);Check(hands.MousePose==0,"release returns to rest");
        }
        hands.MouseButtons(1,0);hands.MouseButtons(2,0);Frames(1);
        Check(hands.MousePose==FingerPose.Index,"sub-frame mouse click visible");Frames(1);
        hands.MouseButtons(0x10,0);hands.MouseButtons(0x800,120);Frames(1);
        Check(hands.MousePose==0,"middle click and horizontal scroll do not animate");
        hands.MouseButtons(4,0);hands.Key(0x24,false);hands.MouseButtons(0x401,120);Frames(20);
        Check(pointer.Contact&&hands.MousePose==0,"typing owns pointer paw");
        hands.Key(0x24,false,true);Frames(120);Check(!pointer.Away&&hands.MousePose==0,"no deferred mouse pose after typing");
        hands.MouseButtons(5,0);hands.Reset();Frames(1);Check(hands.MousePose==0,"reset clears held and pending fingers");
        Check(Math.Abs(1052+kb.X-1154)<8,"resting keyboard paw touches the right edge");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-rest.png");
        foreach(var (left,right) in new[]{(0x07,0x08),(0x15,0x16),(0x23,0x24),(0x30,0x31)}){
            hands.Reset();hands.Key(left,false);Frames(20);
            Check(kb.Contact&&!pointer.Away,"6/Y/H/B use the keyboard hand");
            hands.Key(right,false);Frames(20);
            Check(kb.Contact&&pointer.Contact&&930+kb.X>315+pointer.X,"7/U/J/N join on the mirrored side");
        }
        var (nearLeft,nearRight)=hands.DrawOffsets();
        Check(Math.Abs(DeskHands.KeyboardCenter+nearRight-DeskHands.PointerCenter-nearLeft-DeskHands.MinimumGap)<.1,"B/N share the minimum clearance");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-boundary.png");
        hands.Reset();hands.Cursor(9000);Frames(50);
        var parked=hands.MouseX;Check(parked>27,"mouse moves with its own hand when parked");
        hands.Key(0x1e,false);hands.Key(0x1c,false);hands.Step(1f/30);
        Check(pointer.Y is >= -16 and < -1&&!pointer.Contact,"mouse hand lifts on its way to Enter");
        Frames(30);
        Check(kb.X>85&&Math.Abs(315+pointer.X-510)<.1&&kb.Contact&&pointer.Contact,"A on screen right and Enter on screen left can strike together");
        Diagnostics.Save(after.Draw(352,352,332,20,20),"desk-chord.png");
        for(int i=0;i<90;i++){hands.Key(0x1c,false);hands.Cursor(-500);hands.Step(1f/30);}
        Check(kb.Contact&&pointer.Contact&&hands.MouseX==parked,"held keys stay down without dragging the mouse");
        hands.Key(0x1c,false,true);Frames(25);
        Check(!pointer.Contact&&kb.Contact&&Math.Abs(pointer.X-195)<.1,"release lifts only the typing mouse hand and delays its return");
        Frames(90);Check(!pointer.Away&&pointer.X==hands.MouseX&&kb.Contact,"mouse hand returns to the parked mouse independently");
        hands.Key(0x1e,false,true);Frames(100);
        Check(!kb.Away&&kb.X==DeskHands.KeyboardHome,"keyboard hand returns to keyboard edge");
        hands.Key(0x24,false);Frames(30);hands.Key(0x25,false);Frames(1);
        Check(!pointer.Contact&&Math.Abs(pointer.Y)<.1,"adjacent J to K rearticulates without a wrist hop");
        hands.Key(0x24,false,true);Frames(2);Check(pointer.Contact,"release of J does not cancel held K");
        hands.Key(0x25,false,true);Frames(1);hands.Key(0x25,false);hands.Key(0x25,false,true);Frames(1);
        Check(pointer.Contact,"sub-frame tap displayed");Frames(1);Check(!pointer.Contact,"sub-frame tap releases");
        hands.Enabled=false;hands.Reset();
        foreach(bool peripherals in new[]{false,true}){
            hands.Enabled=peripherals;
            int pad=(int)Math.Ceiling(192d*Renderer.PlacardLeftInset/1254);
            byte[] Frame(Pose p)=>Pixels(after.DrawState(192+pad,192,192,pad,0,p,0,0));
            var neutral=Frame(new Pose());
            byte[] Paddle(double t)=>Frame(new Pose{CelId="complete",CelTime=t});
            Check(neutral.SequenceEqual(Paddle(0))&&neutral.SequenceEqual(Paddle(Renderer.PlacardSeconds)),
                $"pickup and release match the resting hand: peripherals={peripherals}");
            var held=Paddle(1.5);
            Check(held.SequenceEqual(Paddle(1.17))&&held.SequenceEqual(Paddle(2.65))&&
                !held.SequenceEqual(Paddle(1.06))&&!held.SequenceEqual(Paddle(2.77)),
                $"paddle holds the same full-raised pose for 1.5 seconds: peripherals={peripherals}");
            Check(Enumerable.Range(0,held.Length/4).Count(i=>held[4*i+1]>held[4*i+2]+45&&held[4*i+3]>200)>24,
                $"green check visible when held: peripherals={peripherals}");
            Check(Enumerable.Range(0,192).All(y=>held[4*y*(192+pad)+3]==0),"paddle clears the left render edge");
        }
        hands.Enabled=false;
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
