using System.IO;
using System.Numerics;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal static class HeadTouchDiagnostics
{
    static void Check(bool pass,string what){if(!pass)throw new InvalidOperationException(what);}
    internal static void Run(){
        var c=new MotionController();float Now()=>(float)c.Clock;
        void Tick(int count){for(int i=0;i<count;i++)c.Step(1d/60,default,false);}
        var g=c.HeadTouch;g.Begin(Now());g.Move(Now(),new(1,0));Tick(60);
        Check(c.State=="head-touch"&&g.Mode=="rub"&&g.Angle>1,"rightward stroke");
        float held=g.Angle;Tick(45);Check(Math.Abs(held-g.Angle)<.02,"stationary does not oscillate");
        g.Move(Now(),new(-1,0));Tick(40);Check(g.Angle< -2,"stroke reverses");
        Check(!g.End(Now()),"rub must not open panel");Tick(50);Check(c.State=="idle","release settles");
        for(int direction=-1;direction<=1;direction+=2){
            g.Begin(Now());
            // A whole path can arrive between paints. Classification must still see its orientation and extent.
            for(int i=1;i<=24;i++){float a=direction*i*MathF.Tau/24;g.Move(Now(),new(MathF.Sin(a),1-MathF.Cos(a)));}
            Tick(30);Check(g.Mode=="circle"&&g.Pitch<-.5f,"circle back at its top");
            g.Move(Now(),new(0,2));Tick(30);Check(g.Pitch>.5f,"circle tracks vertical reversal");
            g.End(Now());Tick(50);
        }
        g.Begin(Now());Tick(40);Check(g.Mode=="press"&&g.Y>45,"hold");g.End(Now());Tick(50);
        g.Begin(Now());Tick(3);Check(g.End(Now()),"single menu candidate");Tick(6);
        g.Begin(Now());Tick(4);Check(g.Squash>.15f&&!g.End(Now()),"burst consumes menu and squashes");
        c.CancelTouch();Tick(2);Check(c.State!="head-touch","capture cancellation");
        c.React("wink");Tick(1);c.HeadTouch.Begin(Now());c.HeadTouch.Move(Now(),new(1,0));Tick(5);
        Check(c.State=="wink","do not interrupt wink");
        var output=Environment.GetEnvironmentVariable("CLAUDE_GESTURE_FRAMES");
        if(!string.IsNullOrEmpty(output))Render(output);
    }
    private static void Render(string output){
        Directory.CreateDirectory(output);
        using var r=new Renderer(Path.Combine(AppContext.BaseDirectory,"assets","motion"));r.Hands.Enabled=true;
        var states=Enumerable.Range(0,4).Select(_=>new MotionController()).ToArray();
        bool[] held=new bool[4];
        for(int f=0;f<126;f++){
            using var sheet=new SKBitmap(896,884);using var canvas=new SKCanvas(sheet);canvas.Clear(SKColor.Parse("#202022"));
            for(int i=0;i<4;i++){
                var c=states[i];var t=(float)c.Clock;
                var down=t>=.38f&&t<3.12f;
                if(i==3)down=new[]{.38f,.67f,.96f,1.26f,1.56f,1.83f,2.10f,2.39f,2.68f,2.99f}.Any(p=>t>=p&&t<p+.09f);
                var a=(Math.Max(0,t-.38f))*MathF.Tau*.86f;
                var point=i switch{0=>new Vector2(.84f*MathF.Tanh(2.5f*MathF.Sin(a*.79f)),.03f*MathF.Sin(2*a)),
                    1=>new(.61f*MathF.Sin(a),.49f*(1-MathF.Cos(a))),_=>Vector2.Zero};
                if(down&&!held[i])c.HeadTouch.Begin(t);
                if(down)c.HeadTouch.Move(t,point);
                if(!down&&held[i])c.HeadTouch.End(t);held[i]=down;
                var pose=c.Step(1d/30,default,false);
                using var tile=new SKBitmap(448,442);using var tc=new SKCanvas(tile);
                if(c.State=="head-touch")r.DrawTouch(tc,pose,c.HeadTouch,384f/1254,104,151);
                else r.Draw(tc,pose,c.Hair,c.Tip,384f/1254,104,151,front:c.FrontHair);
                int x=i%2*448,y=i/2*442;canvas.DrawBitmap(tile,x,y,new SKSamplingOptions(SKFilterMode.Nearest));
                if(down){
                    using var dot=new SKPaint{IsAntialias=true,Color=SKColor.Parse("#562e23")};
                    float px=x+224+60*point.X,py=y+84+32*point.Y;
                    canvas.DrawCircle(px,py,8,dot);dot.Color=SKColor.Parse("#fff5d7");canvas.DrawCircle(px,py,5,dot);
                }
            }
            using var png=sheet.Encode(SKEncodedImageFormat.Png,100);using var dst=File.Create(Path.Combine(output,$"{f:D3}.png"));png.SaveTo(dst);
        }
    }
}
