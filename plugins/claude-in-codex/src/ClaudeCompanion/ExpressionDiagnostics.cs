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
        var rig=CharacterRig.Load(MotionRaster.Assets,true).CreateBones();
        var pin=rig[11].Rest;
        MenuPitchProjection At(float weight)=>new(weight,rig[1].Rest,pin);
        var zero=At(0);var mid=At(.5f);var peak=At(1);
        Vector2 crown=new(627,45),mark=new(627,120),root=new(627,206),chin=new(627,1067),overlap=new(627,730);
        float drop=peak.Cap(crown).Y,lowerDrop=peak.Cap(mark).Y,rootDrop=peak.Cap(root).Y;
        check(drop is >58 and <63&&drop>lowerDrop&&lowerDrop>rootDrop&&lowerDrop>peak.Cap(new(885,120)).Y,
            "crown recedes most at the top and eases toward the roots and temples");
        check(mid.Cap(crown).Y is >0 and <58&&mid.Cap(crown).Y<drop&&peak.Cap(root)==peak.Backing(root)&&peak.Cap(root)==peak.Fringe(root),
            "the crown eases through intermediate angles while cap, backing and fringe roots stay registered");
        check(peak.Face(chin).Y is < -7 and > -15&&peak.Face(overlap).Y-peak.Fringe(overlap).Y>6,
            "the chin lifts slightly while the overlapping fringe moves independently of the face");
        var nearTip=peak.Side(new(442,940),false);var farTip=peak.Side(new(810,905),true);
        check(nearTip.Y< -12&&nearTip.X<0&&farTip.Y< -12&&farTip.X>0,
            "both foreground tips lift and move subtly outward");
        var flower=peak.Ornament(pin);
        var petal=peak.Ornament(pin+new Vector2(110,90))-flower;
        var ribbon=peak.Ornament(new(1125,1060))-flower;
        check(flower.Y is < -7 and > -18&&flower.X>0&&Math.Abs(petal.X)<5&&Math.Abs(petal.Y)<6&&Math.Abs(ribbon.X)<4&&ribbon.Y==4,
            "the flower follows the temple, with cohesive petals and a hanging ribbon");
        check(peak.Rear(new(220,1150))==new Vector2(0,6)&&peak.Backing(new(180,1060))==peak.Rear(new(180,1060)),
            "lower rear hair drops by six source pixels and keeps its horizontal position");

        static Func<Vector2,Vector2>[] Surfaces(MenuPitchProjection p)=>
            [p.Cap,p.Face,p.Fringe,v=>p.Side(v,false),v=>p.Side(v,true),p.Ornament,p.Backing,p.Rear];
        static float Winding(Vector2 a,Vector2 b,Vector2 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        check(Surfaces(zero).All(map=>new[]{crown,root,overlap,chin,pin}.All(p=>map(p)==default)),
            "neutral is identity on every menu surface");
        bool stable=true;
        foreach(var phase in new[]{At(.25f),mid,At(.75f),peak})foreach(var map in Surfaces(phase))
            for(int y=0;y<1254;y+=26)for(int x=0;x<1254;x+=26)
            {
                Vector2 A=new(x,y),B=new(x+26,y),C=new(x+26,y+26),D=new(x,y+26);
                A+=map(A);B+=map(B);C+=map(C);D+=map(D);
                stable&=Winding(A,B,C)>0&&Winding(A,C,D)>0;
            }
        check(stable,"no projected surface folds during lift or return");
    }
}
