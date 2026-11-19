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
        var g=c.HeadTouch;g.Begin(Now());g.Move(Now(),new(1,0));
        Check(g.TipRoll==0,"mouse sampling alone must not move the tips");Tick(8);
        Check(g.Angle>0&&g.TipRoll>1,"tip trails the moving attachment");CheckRenderedLag(g);Tick(52);
        Check(c.State=="head-touch"&&g.Mode=="rub"&&g.Angle>1,"rightward stroke");
        float held=g.Angle;Tick(45);Check(Math.Abs(held-g.Angle)<.02,"stationary does not oscillate");
        Check(Math.Abs(g.TipRoll)<.02,"tips settle while head remains tilted");
        g.Move(Now(),new(-1,0));Tick(9);Check(g.TipRoll< -1,"tip inertia reverses with the head");
        Tick(31);Check(g.Angle< -2,"stroke reverses");
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
        g.Begin(Now());Tick(3);Check(g.End(Now())&&!g.Playing(Now()),"first click immediately opens menu without squashing");
        c.LookAtMenu(new(0,-1));Tick(1);Check(c.State=="menu","menu launched");
        g.Begin(Now());Tick(2);Check(!g.End(Now(),menuOpen:true),"click in open menu must not toggle it");Tick(4);
        Check(c.State=="head-touch"&&g.Squash>.15f,"next click squashes during menu animation");
        Tick(90);g.Begin(Now());Tick(2);g.End(Now(),menuOpen:true);Tick(4);
        Check(g.Squash>.15f,"open-menu squash does not require a double click");
        c.CancelTouch();Tick(2);Check(c.State!="head-touch","capture cancellation");
        c.React("wink");Tick(1);c.HeadTouch.Begin(Now());c.HeadTouch.Move(Now(),new(1,0));Tick(5);
        Check(c.State=="wink","do not interrupt wink");
        var busy=new MotionController();busy.ObserveTasks(new(1,true,false));
        for(int i=0;i<125;i++)busy.Step(1d/60,default,false);
        Check(busy.State=="thinking","mid thinking phrase");
        busy.LookAtMenu(new(0,-1));busy.Step(1d/60,default,false);
        Check(busy.State=="menu","menu overrides thinking on next frame");
        for(int i=0;i<100;i++)busy.Step(1d/60,default,false);
        Check(busy.State=="thinking","unfinished task resumes thinking");
        var output=Environment.GetEnvironmentVariable("CLAUDE_GESTURE_FRAMES");
        if(!string.IsNullOrEmpty(output))Render(output);
    }
    private static void CheckRenderedLag(HeadTouch g){
        using var r=new Renderer(Path.Combine(AppContext.BaseDirectory,"assets","motion"));
        using var moving=new SKBitmap(384,384);using var rigid=new SKBitmap(384,384);
        var pose=g.Pose(0);var (roll,lift)=(g.TipRoll,g.TipLift);
        using(var c=new SKCanvas(moving))r.DrawTouch(c,pose,g,384f/1254,0,0);
        g.TipRoll=g.TipLift=0;
        using(var c=new SKCanvas(rigid))r.DrawTouch(c,pose,g,384f/1254,0,0);
        (g.TipRoll,g.TipLift)=(roll,lift);
        int upper=0,lower=0;
        for(int y=0;y<384;y++)for(int x=0;x<384;x++){
            if(moving.GetPixel(x,y)==rigid.GetPixel(x,y))continue;
            if(y<145)upper++;if(y>=215)lower++;
        }
        Check(upper==0&&lower>120,$"same head pose: crown changed {upper} pixels, tips changed {lower}");
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
                if(down&&!held[i])c.HeadTouch.Begin(t,menuOpen:i==3&&t>.65f);
                if(down)c.HeadTouch.Move(t,point);
                if(!down&&held[i])c.HeadTouch.End(t,menuOpen:i==3&&t>.65f);held[i]=down;
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
