using ClaudeCompanion;
using MotionLab;
using SkiaSharp;
using System.IO;
using System.Windows;

internal static class MenuKeyform
{
    public static void Run(string root,Renderer r,int size)
    {
        var dir=Path.Combine(root,"artifacts/menu-keyform");Directory.CreateDirectory(dir);
        void Pair(string name,SKBitmap a,SKBitmap b){
            using var board=new SKBitmap(2*size,size);using var c=new SKCanvas(board);
            c.Clear(SKColors.Transparent);var s=new SKSamplingOptions(SKFilterMode.Nearest);
            c.DrawBitmap(a,0,0,s);c.DrawBitmap(b,size,0,s);
            using var data=board.Encode(SKEncodedImageFormat.Png,100);
            using var f=File.Create(Path.Combine(dir,name+".png"));data.SaveTo(f);
        }
        var neutral=new Pose();
        using(var a=r.Render(neutral,0,0,size))
        using(var b=r.MenuPitch(neutral,0,0,size,default,12,true,true))
            Pair("fixed-gaze",a,b);
        var old=new MotionController();var quiet=new MotionController();var cursor=new Vector(.12,0);
        for(int i=0;i<72;i++){
            if(i==8)old.LookAtMenu(new(-.25,-.95));
            var p=old.Step(1d/30,cursor,true);
            var basePose=quiet.Step(1d/30,cursor,true);
            var t=(i-8)/30d;
            var oldAngle=12*Acting.Beat(t,.08,.36,.66,1.18)-.7f*Acting.Beat(t,.86,1.08,1.12,1.38);
            using var a=r.MenuPitch(p,old.Hair,old.Tip,size,old.FrontHair,oldAngle,true);
            var pitch=Acting.Beat(t,2d/30,.36,.80,1.28);
            var gaze=Acting.Beat(t,0,.19,.80,1.28);
            var q=basePose with{GazeX=.12f-.37f*gaze,GazeY=-.95f*gaze,EyeRoll=.95f*gaze,
                BrowLeft=-7*pitch,BrowRight=3*pitch};
            using var b=r.MenuPitch(q,quiet.Hair,quiet.Tip,size,quiet.FrontHair,12*pitch,true,true);
            Pair($"{i:000}",a,b);
        }
    }
}
