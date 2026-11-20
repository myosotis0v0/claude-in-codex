using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vector2=System.Numerics.Vector2;
using MotionLab;

namespace ClaudeCompanion;

internal static class ExpressionDiagnostics
{
    internal static void Run(bool baseline)
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("An isolated test output is required.");
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"pat"));
        if(baseline&&Motions.Evaluate("pat",.2).EyeLeft<.8f)throw new InvalidOperationException("Baseline requires the original slow pat expression.");
        var pet=new PetView{ArtworkSize=224,RenderInset=new Vector(14,14)};
        if(!pet.EnableMotion())throw new InvalidDataException("Actual material did not load.");
        pet.Measure(new Size(238,238));pet.Arrange(new Rect(0,0,238,238));var now=DateTimeOffset.UtcNow;
        try
        {
            pet.Controller!.React("pat");var poses=new List<Pose>();
            for(var i=0;i<180;i++)
            {
                pet.SetFixedGaze(new Vector(.25,-.1));pet.Animate(1/30d,true,now.AddSeconds((i+1)/30d));
                poses.Add(pet.Controller.Current);Diagnostics.Save(Diagnostics.Render(pet,238,238),Path.Combine("pat",$"{i:D3}.png"));
            }
            File.WriteAllText(Path.Combine(output,"pat-poses.json"),JsonSerializer.Serialize(poses,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{pet.DisposeMotion();}
        if(!baseline)
        {
            var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new InvalidDataException(name);checks.Add(name);}
            CheckMenuProjection(Check);
            Check(PatEyes.Closure(.12)>.999&&PatEyes.Closure(3)>.999,"pat closes in 120ms and still has a held-closed expression");
            Check(PatEyes.Closure(4.56)<.001,"pat reopening finishes in 240ms, independent of the body curve");
            var menu=new PetView{ArtworkSize=224,RenderInset=new Vector(14,14)};
            Check(menu.EnableMotion(),"menu preview uses packaged-style material and controller");
            menu.Measure(new Size(238,238));menu.Arrange(new Rect(0,0,238,238));Directory.CreateDirectory(Path.Combine(output,"menu"));
            var menuPoses=new List<Pose>();
            try
            {
                for(var i=0;i<120;i++)
                {
                    if(i==15)menu.Controller!.LookAtMenu(new Vector(-.25,-.95));
                    menu.SetFixedGaze(new Vector(.25,.1));menu.Animate(1/30d,true,now.AddSeconds(i/30d));var pose=menu.Controller!.Current;
                    Check(pose.Mouth=="neutral"&&pose.MouthMix==0&&pose.Blush==0&&pose.SmileEyes==0&&pose.SoftSmileEyes==0,
                        $"menu frame {i} retains the unimpressed mouth and eyes");
                    if(i==30)
                    {
                        Check(menu.Controller.State=="menu"&&pose.GazeY<-.9,"menu action reaches its actual upper target");
                        Check(pose.EyeRoll>.9,"upward menu glance lifts the iris independently of the upper lash");
                        Check(pose.MenuPitchWeight>.99&&pose.BrowLeft==-7*pose.MenuPitchWeight&&pose.BrowRight==3*pose.MenuPitchWeight,"brows and the lifted face share the peak keyform");
                    }
                    var gaze=GazeMotion.Map(new Vector(.25,.1));
                    if(i==80)Check(menu.Controller.State=="idle"&&Math.Abs(pose.GazeX-gaze.X)<.01&&Math.Abs(pose.GazeY-gaze.Y)<.01&&pose.EyeRoll==0,"menu action automatically restores cursor gaze and clears the eye roll");
                    Diagnostics.Save(Diagnostics.Render(menu,238,238),Path.Combine("menu",$"{i:D3}.png"));menuPoses.Add(pose);
                }
                foreach(var direction in new[]{new Vector(.7,-.8),new Vector(-.7,.8)})
                {
                    for(var i=0;i<45;i++)menu.Controller!.Step(1/30d,new Vector(),true);
                    menu.Controller!.LookAtMenu(direction);
                    for(var i=0;i<16;i++)menu.Controller.Step(1/30d,new Vector(),true);
                    Check(Math.Abs(menu.Controller.Current.GazeX-direction.X)<.01&&Math.Abs(menu.Controller.Current.GazeY-direction.Y)<.01,"menu glance accepts the real popup direction rather than a hardcoded upper-left target");
                    Check(Math.Abs(menu.Controller.Current.EyeRoll-Math.Max(0,-direction.Y))<.01,"a below-character popup never triggers an upward eye roll");
                    Check((menu.Controller.Current.MenuPitchWeight>0)==(direction.Y<0),"only a popup above the character lifts the chin");
                }
                var normal=new MotionController();normal.React("pat");
                for(var i=0;i<4;i++)normal.Step(1/30d,new Vector(),false);
                Check(normal.Current.EyeLeft<.001&&normal.Current.SoftSmileEyes>.999,"production-controller blend does not stretch the short pat closure back to 280ms");
                Directory.CreateDirectory(Path.Combine(output,"posters"));
                using(var surface=new MotionSurface())
                {
                    surface.Renderer.Hands.Enabled=true;
                    var fixedGaze=new Pose{GazeX=.1f,GazeY=-.08f};
                    foreach(var (name,weight) in new[]{("neutral",0f),("half",.5f),("peak",1f)})
                        Diagnostics.Save(surface.DrawState(222,208,192,18,16,fixedGaze with{MenuPitchWeight=weight},0,0),"fixed-pitch-"+name+".png");
                    surface.Renderer.Hands.Enabled=false;
                    var keyframes=Path.Combine(output,"menu-keyform");Directory.CreateDirectory(keyframes);
                    for(int i=0;i<72;i++){
                        if(i==8)surface.Controller.LookAtMenu(new(-.25,-.95));
                        var p=surface.Controller.Step(1d/30,new(.12,0),true);
                        if(i==9)Check(p.GazeY<0&&p.MenuPitchWeight==0,"eyes lead the head before the pitch starts");
                        using var rendered=surface.Renderer.Render(p,surface.Controller.Hair,surface.Controller.Tip,512,front:surface.Controller.FrontHair);
                        using var data=rendered.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
                        using var file=File.Create(Path.Combine(keyframes,$"{i:D3}.png"));data.SaveTo(file);
                    }
                    foreach(var item in Motions.Catalog)
                    {
                        var character=surface.DrawState(300,300,280,20,20,Motions.Evaluate(item.Id,2.8),0,0);
                        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                            PetView.DrawMotionLayers(dc,character,new Rect(0,0,300,300),new Point(26,24),1,item.Id is "thinking" or "complete"?1:0,
                                new SolidColorBrush(Color.FromRgb(32,32,34)),item.Id);
                        var bitmap=new RenderTargetBitmap(300,300,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                        Diagnostics.Save(bitmap,Path.Combine("posters",item.Id+".png"));
                    }
                    Diagnostics.Save(surface.DrawState(300,300,280,20,20,new Pose(),0,0),"neutral.png");
                }
                File.WriteAllText(Path.Combine(output,"menu-poses.json"),JsonSerializer.Serialize(menuPoses,new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(Path.Combine(output,"expression-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,PatFrames=180,MenuFrames=120,Fps=30,
                    Scope="Original pat baseline captured before changing the expression. Candidate controller and actual WPF offscreen composition; not installed, no desktop input. Native menu opening remains manual verification."},new JsonSerializerOptions{WriteIndented=true}));
            }
            finally{menu.DisposeMotion();}
        }
        Console.WriteLine(baseline?"Original pat captured":"Candidate pat and menu captured");
    }

    private static void CheckMenuProjection(Action<bool,string> check)
    {
        var rest=new MenuPitchProjection(0);
        var mid=new MenuPitchProjection(.5f);
        var full=new MenuPitchProjection(1);
        Vector2 crown=new(627,48),hairline=new(627,275),eye=new(627,799),chin=new(627,1064);
        check(full.HeadWarp(crown).Y is >30 and <36&&full.HeadWarp(hairline).Y is < -17 and > -24,
            "crown recedes and the hairline moves up, instead of translating the whole head");
        check(full.HeadWarp(new(300,210)).X>0&&full.HeadWarp(new(950,210)).X<0,
            "crown shrinks slightly in width");
        check(full.HeadWarp(chin).Y is < -9 and > -15&&full.HeadWarp(eye).Y<full.HeadWarp(chin).Y,
            "chin lifts a little and does not follow the full eye travel");
        foreach(var p in new[]{crown,hairline,eye,chin})
        {
            check(rest.HeadWarp(p)==default&&mid.HeadWarp(p)*2==full.HeadWarp(p),"neutral and intermediate keyforms keep the same pitch phase");
        }
        foreach(var p in new[]{new Vector2(205,922),new(1049,922)})
        {
            var drape=full.SideLock(p);var shift=drape+full.HeadWarp(p+drape);
            check(shift.Y is < -6 and > -15&&Math.Abs(shift.X)<5&&rest.SideLock(p)==default,
                "both side locks lift gently without the tips spreading outward");
        }
        var pin=new Vector2(1015,368);
        check(full.SideLock(new(285,440))==default&&full.Accessory(pin)==full.HeadWarp(pin)&&!Renderer.MenuUsesHeadWarp("accessory"),
            "the roots stay attached, and the flower shares the temple position without a second head warp");
        var petals=full.Accessory(pin+new Vector2(110,90))-full.Accessory(pin);
        var ribbon=full.Accessory(new(1125,1060));
        check(Math.Abs(petals.X)<4&&Math.Abs(petals.Y)<4&&ribbon.Y is < -9 and > -15&&Math.Abs(ribbon.X)<9,
            "petals keep their shape and long ribbons keep their drape");
        bool monotone=true;
        for(int y=0;y<1254;y+=8)
            monotone&=8+full.HeadWarp(new(627,y+8)).Y-full.HeadWarp(new(627,y)).Y>0;
        check(monotone,"the pitch mesh never folds its horizontal bands");
        foreach(var layer in new[]{"rear-hair","hair-backing","hair-curtain"})
            check(!Renderer.MenuUsesHeadWarp(layer),"rear layers bypass the forward pitch mesh: "+layer);
        foreach(var x in new[]{120,627,1140})
        {
            check(full.RearHair(new(x,600))==default&&rest.RearHair(new(x,1040))==default,"rear anchor stays in place");
            var lower=full.RearHair(new(x,1040));
            check(lower.X==0&&lower.Y==7,"rear ends lower only, by seven source pixels");
        }
        check(full.Backing(new(627,80))==full.HeadWarp(new(627,80))&&full.Backing(new(180,1060))==full.RearHair(new(180,1060)),
            "upper backing follows the crown without a duplicate outline while its lower section stays with the rear hair");
    }
}
