using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MotionLab;

namespace ClaudeCompanion;

internal static class RenderThreadDiagnostics
{
    private static readonly TimeSpan Limit=TimeSpan.FromSeconds(15);
    internal static async Task Run()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??throw new InvalidOperationException("Set an isolated output directory.");
        Directory.CreateDirectory(output);
        await VerifyMailbox();
        await VerifyView();
        var results=new List<object>();
        foreach(var size in new[]{200,280,400})
        {
            results.Add(await Measure(size,false));
            results.Add(await Measure(size,true));
        }
        File.WriteAllText(Path.Combine(output,"render-thread-report.json"),JsonSerializer.Serialize(new{
            Success=true,Results=results,
            Scope="Offscreen WPF dispatcher; same Animate/DrawAnimated calls as production. Includes UI animation sampling, drawing commands and pixel submission, not native input, layout, compositor or GPU. 33 ms target in both modes; PresentsPerSecond counts accepted character bitmaps, not screen refresh."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void Check(bool pass,string reason){if(!pass)throw new InvalidOperationException(reason);}
    private static async Task WaitFor(Func<bool> done)
    {
        var sw=Stopwatch.StartNew();
        while(!done())
        {
            if(sw.Elapsed>Limit)throw new TimeoutException("Offscreen render did not arrive.");
            await Task.Delay(5);
        }
    }
    private static async Task VerifyMailbox()
    {
        var seen=new ConcurrentQueue<int>();int notify=0;
        var uiThread=Environment.CurrentManagedThreadId;
        using var first=new ManualResetEventSlim();
        using var last=new ManualResetEventSlim();
        var profile=CharacterRig.Load(MotionRaster.Assets,true);
        using var worker=new LatestMotionRenderer(profile,Dispatcher.CurrentDispatcher,()=>notify++,frame=>{
            Check(uiThread!=Environment.CurrentManagedThreadId,"Raster ran on the UI thread.");
            var id=(int)frame.Pose.HeadX;seen.Enqueue(id);
            if(id==1)Check(first.Wait(Limit),"First frame was not released.");
            if(id==20)Check(last.Wait(Limit),"Old epoch frame was not released.");
        });
        var geometry=new MotionGeometry(200,300,200,0,100);
        MotionFrame Frame(int i)=>new(geometry,new Pose{HeadX=i},0,0,default,null,default);
        worker.Submit(Frame(1));await WaitFor(()=>seen.Contains(1));
        for(int i=2;i<=20;i++)worker.Submit(Frame(i));
        first.Set();await WaitFor(()=>seen.Contains(20)&&notify>0);
        Check(seen.SequenceEqual(new[]{1,20}),"Superseded requests were rendered.");
        worker.Discard();
        Check(worker.TakeLatest()==null,"Discard did not remove the completed frame.");
        worker.Submit(Frame(21));last.Set();
        using var presenter=new MotionPresenter();
        WriteableBitmap? image=null;
        await WaitFor(()=>{
            if(worker.GetFailure() is {} error)throw error;
            presenter.Offer(worker.TakeLatest());return (image=presenter.Present(geometry))!=null;
        });
        using var reference=new MotionSurface();
        var expected=reference.DrawState(200,300,200,0,100,Frame(21).Pose,0,0);
        Check(Pixels(image!).SequenceEqual(Pixels(expected)),"Epoch invalidation returned stale pixels.");
        var bounds=new Rect(0,0,200,300);
        Check(!presenter.Hit(new Point(0,0),geometry,bounds)&&presenter.Hit(new Point(110,240),geometry,bounds),
            "Hit testing did not use the displayed pixels.");
        worker.Dispose();await worker.Completion.WaitAsync(Limit);
    }
    private static async Task VerifyView()
    {
        var sync=new PetView();var worker=new PetView();
        try
        {
            Check(sync.EnableMotion()&&worker.EnableMotion(useBackground:true),"Both pipelines must load packaged assets.");
            var flat=new MotionGeometry(215,300,200,15,100);
            var scaled=new MotionGeometry(330,450,300,30,150);
            foreach(var (view,size) in new[]{(sync,flat),(worker,flat)})
            {
                view.ArtworkSize=size.ArtworkPixels;view.RenderInset=new Vector(size.InsetX,size.InsetY);
                view.Hands!.Enabled=true;
                view.Hands.MouseButtons(5,0);view.Animate(1d/30,true);view.Look(new Vector(.4,-.8));
            }
            async Task Compare(MotionGeometry size)
            {
                var previous=worker.DisplayedFrames;
                var before=Pixels(sync.DrawCharacter(size)!);WriteableBitmap? after=null;
                await WaitFor(()=>{
                    after=worker.DrawCharacter(size);
                    return worker.DisplayedFrames>previous;
                });
                Check(after!=null&&Pixels(after).SequenceEqual(before),"Background pixels differ from synchronous pixels.");
            }
            await Compare(flat);
            foreach(var view in new[]{sync,worker})
            {
                view.Controller!.LookAtMenu(new Vector(.3,-.8));
                for(int i=0;i<16;i++)view.Animate(1d/30,true);
                Check(view.Controller.Current.MenuPitchWeight>0,"Animated comparison did not enter the menu mesh path.");
            }
            await Compare(scaled);
            foreach(var view in new[]{sync,worker})
            {
                var touch=view.Controller!.HeadTouch;var start=(float)view.Controller.Clock;
                touch.Begin(start);touch.Move(start+.01f,new System.Numerics.Vector2(.65f,.06f));
                view.Animate(1d/30,true);
                Check(view.Controller.State=="head-touch"&&touch.Connected,"Animated comparison did not enter the connected touch path.");
            }
            await Compare(flat);
        }
        finally{worker.DisposeMotion();sync.DisposeMotion();}
    }
    private static async Task<object> Measure(int pixels,bool background)
    {
        var geometry=new MotionGeometry(pixels+pixels/10,pixels+100,pixels,pixels/10,100);
        var bounds=new Rect(0,0,geometry.Width,geometry.Height);
        var view=new PetView{ArtworkSize=pixels,RenderInset=new Vector(geometry.InsetX,geometry.InsetY)};
        try
        {
            Check(view.EnableMotion(background),"Unable to start benchmark renderer.");
            view.Hands!.Enabled=true;var visual=new DrawingVisual();
            int syncPresentations=0;
            double Step(int i)
            {
                var start=Stopwatch.GetTimestamp();
                view.Look(new Vector(Math.Sin(i*.16)*.7,Math.Cos(i*.17)*.7));
                view.Animate(.033,true);
                using(var dc=visual.RenderOpen())view.DrawAnimated(dc,geometry,bounds);
                if(!background)syncPresentations++;
                return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            for(int i=0;i<8;i++)Step(i);
            if(background)await WaitFor(()=>{Step(9);return view.DisplayedFrames>0;});
            var samples=new List<double>();
            int Presents()=>background?view.DisplayedFrames:syncPresentations;
            int initial=Presents();var clock=Stopwatch.StartNew();
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
            timer.Tick+=(_,_)=>
            {
                try
                {
                    samples.Add(Step(samples.Count+10));
                    if(samples.Count==75){timer.Stop();done.TrySetResult();}
                }
                catch(Exception error){timer.Stop();done.TrySetException(error);}
            };
            timer.Start();
            try{await done.Task.WaitAsync(Limit);}
            finally{timer.Stop();}
            double elapsed=clock.Elapsed.TotalSeconds;int presented=Presents()-initial;
            samples.Sort();return new{Pixels=pixels,Background=background,Samples=samples.Count,
                UiMedianMs=samples[samples.Count/2],UiP95Ms=samples[(int)(samples.Count*.95)],
                Presents=presented,Elapsed=elapsed,PresentsPerSecond=presented/elapsed};
        }
        finally{view.DisposeMotion();}
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];
        bitmap.CopyPixels(pixels,stride,0);return pixels;
    }
}
