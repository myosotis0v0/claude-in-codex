using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeCompanion;

// Strictly offscreen: no windows, host discovery, settings, IPC or cursor access.
internal static class GazePreview
{
    internal static void Run(bool lashComparison=false)
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")
            ??throw new InvalidOperationException("Set CLAUDE_TEST_OUTPUT to a preview directory.");
        Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var before=new PetView{AnimateLashes=false};var after=new PetView{AnimateLashes=lashComparison};var limits=after.Limits;
        Check(limits==new GazeLimits(20,18,10),"outward 20, inward unchanged at 18, vertical 10 source pixels");
        Check(GazeMotion.Map(default)==default,"neutral gaze unchanged");
        Check(GazeMotion.Map(new Vector(0.25,0)).X>0.25,"near-field response increased");
        var previous=0d;
        foreach(var distance in new[]{0.1,0.5,1,2,4,8,100})
        {
            var mapped=GazeMotion.Map(new Vector(distance,0));
            Check(mapped.X>previous&&mapped.X<1,$"continuous bounded response at distance {distance}");
            previous=mapped.X;
        }
        foreach(var raw in new[]{new Vector(-5,-2),new Vector(5,2),new Vector(-0.1,0.3)})
        {
            var mapped=GazeMotion.Map(raw);
            Check(Math.Abs(Vector.CrossProduct(raw,mapped))<1e-10,"radial mapping preserves cursor direction");
            Check(mapped.Length<1,"mapped vector remains inside motion envelope");
        }
        var innerLeft=GazeMotion.Offset(new Vector(1,0),true,limits);
        var innerRight=GazeMotion.Offset(new Vector(-1,0),false,limits);
        Check(innerLeft.X==18&&innerRight.X==-18,"neither eye increases travel toward nose");
        Check(GazeMotion.Offset(new Vector(-1,0),true,limits).X==-20
            &&GazeMotion.Offset(new Vector(1,0),false,limits).X==20,"both eyes allow only a small outward increase");
        Check(GazeMotion.Offset(new Vector(0,1),true,limits).Y==10
            &&GazeMotion.Offset(new Vector(0,-1),true,limits).Y==-10,"up/down limits increase symmetrically");
        if(lashComparison)VerifyLashes(before,after,Check);else VerifyFixedPixels(after,Check);

        var keys=new[]{new Vector(-6,-1.2),new Vector(-4,-4),new Vector(-1.2,-6),
            new Vector(0,0),new Vector(-3,0),new Vector(3,0),new Vector(0,-3),new Vector(0,3),new Vector(-6,-1.2)};
        if(lashComparison)keys=[new(0,0),new(0,-4),new(0,0),new(0,4),new(0,0),new(-3,-3),new(3,3),new(0,0),new(0,0)];
        var legacy=default(Vector);
        for(var i=0;i<120;i++)
        {
            legacy=LegacyStep(legacy,keys[0]);after.Look(keys[0]);if(lashComparison)before.Look(keys[0]);
        }
        var frames=new List<object>();
        for(var frame=0;frame<240;frame++)
        {
            var segment=frame/30;var t=(frame%30)/30d;t=t*t*(3-2*t);
            var raw=keys[segment]+(keys[segment+1]-keys[segment])*t;
            legacy=LegacyStep(legacy,raw);
            if(lashComparison)before.Look(raw);else before.SetEyeOffsets(legacy,legacy);
            after.Look(raw);
            Save(Diagnostics.Render(before,256,256),Path.Combine(output,$"before-{frame:D3}.png"));
            Save(Diagnostics.Render(after,256,256),Path.Combine(output,$"after-{frame:D3}.png"));
            frames.Add(new{Frame=frame,Stage=lashComparison?(segment<2?"Look up / return":segment<4?"Look down / return":"Diagonal / return"):
                segment<3?"Far-field direction":segment<6?"Near / horizontal":"Vertical / return",
                Input=new[]{raw.X,raw.Y},Before=new[]{legacy.X,legacy.Y},
                Left=new[]{after.LeftOffset.X,after.LeftOffset.Y},Right=new[]{after.RightOffset.X,after.RightOffset.Y}});
        }
        Check(frames.Count==240,"240 paired WPF frames rendered from identical 30 Hz cursor input and smoothing");
        File.WriteAllText(Path.Combine(output,"gaze-report.json"),JsonSerializer.Serialize(new{Success=true,Comparison=lashComparison?"Lashes":"Mapping",Checks=checks,Frames=frames,
            Scope="Offscreen WPF only; no windows shown, no cursor/host access, no running companion or installed plugin changed."},
            new JsonSerializerOptions{WriteIndented=true}));
    }

    private static void VerifyLashes(PetView before,PetView after,Action<bool,string> check)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"assets");
        foreach(var size in new[]{224,256,280,336})
        {
            before.SetEyeOffsets(default,default);after.SetEyeOffsets(default,default);
            var a=Pixels(Diagnostics.Render(before,size,size));var b=Pixels(Diagnostics.Render(after,size,size));
            check(a.Zip(b).All(pair=>Math.Abs(pair.First-pair.Second)<=1),$"neutral lash pose matches previous renderer at {size}px");
        }
        var protectedPixels=new bool[1254*1254];
        foreach(var name in new[]{"left","right"})
        {
            var image=PetView.Load(Path.Combine(root,"lids",$"{name}-foreground.png"));var pixels=Pixels(image);
            var origin=name=="left"?new Point(270,710):new Point(700,710);
            for(var y=0;y<image.PixelHeight;y++)for(var x=0;x<image.PixelWidth;x++)
                if(pixels[(y*image.PixelWidth+x)*4+3]>0)protectedPixels[((int)origin.Y+y+10)*1254+(int)origin.X+x+6]=true;
        }
        foreach(var y in new[]{-10d,10d,-5d,5d})
        {
            before.SetEyeOffsets(new Vector(0,y),new Vector(0,y));after.SetEyeOffsets(new Vector(0,y),new Vector(0,y));
            var a=Pixels(Diagnostics.Render(before,1254,1254));var b=Pixels(Diagnostics.Render(after,1254,1254));
            var hair=0;var outside=0;var changed=0;
            for(var py=0;py<1254;py++)for(var px=0;px<1254;px++)
            {
                var i=(py*1254+px)*4;var differs=false;
                for(var c=0;c<4;c++)differs|=Math.Abs(a[i+c]-b[i+c])>1;
                if(!differs)continue;
                changed++;if(protectedPixels[py*1254+px])hair++;
                var sx=px-6;var sy=py-10;
                if(sy<710||sy>=940||!((sx>=270&&sx<565)||(sx>=700&&sx<990)))outside++;
            }
            check(changed>500,$"lash deformation visibly changes eye region at vertical offset {y}");
            check(hair==0,$"fixed hair/outline matte remains unchanged at vertical offset {y}");
            check(outside==0,$"no changes outside the two eye-layer regions at vertical offset {y}");
            Save(Diagnostics.Render(after,256,256),Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")!,$"lash-extreme-{y}.png"));
        }
        foreach(var name in new[]{"left","right"})
        {
            var centerX=name=="left"?144:145;
            (double Center,double Weight) Column(int pose)
            {
                var image=PetView.Load(Path.Combine(root,"lids",$"{name}-pose-{pose:D2}.png"));var pixels=Pixels(image);
                var total=0d;var sum=0d;
                // Include the upward-warped lash edge while excluding the separate crease.
                for(var y=34;y<120;y++){var alpha=pixels[(y*image.PixelWidth+centerX)*4+3];sum+=y*alpha;total+=alpha;}
                return(sum/total,total);
            }
            var neutral=Column(16);var up=Column(0);var down=Column(32);
            check(up.Center<neutral.Center-6&&down.Center>neutral.Center+10,$"{name}: lash center follows up and down gaze");
            check(Math.Abs(up.Weight-neutral.Weight)<4&&Math.Abs(down.Weight-neutral.Weight)<4,$"{name}: lash center keeps thickness instead of scaling the whole eye");
        }
        before.SetEyeOffsets(default,default);after.SetEyeOffsets(default,default);
    }

    private static Vector LegacyStep(Vector gaze,Vector raw)
    {
        var target=new Vector(Math.Clamp(raw.X,-1,1)*18,Math.Clamp(raw.Y,-1,1)*8);
        var next=gaze+(target-gaze)*0.25;
        return (next-gaze).Length<0.05?gaze:next;
    }

    private static void VerifyFixedPixels(PetView pet,Action<bool,string> check)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"assets");
        var allowed=new bool[1254*1254];
        foreach(var name in new[]{"left","right"})
        {
            var mask=new FormatConvertedBitmap(new BitmapImage(new Uri(Path.Combine(root,$"{name}-motion-mask.png"))),PixelFormats.Gray8,null,0);
            var bytes=new byte[allowed.Length];mask.CopyPixels(bytes,1254,0);
            for(var y=0;y<1254;y++)for(var x=0;x<1254;x++)
                if(bytes[y*1254+x]>0&&x+6<1254&&y+10<1254)allowed[(y+10)*1254+x+6]=true;
        }
        pet.SetEyeOffsets(default,default);
        var center=Pixels(Diagnostics.Render(pet,1254,1254));
        foreach(var direction in new[]{new Vector(-1,0),new Vector(1,0),new Vector(0,-1),new Vector(0,1),
            new Vector(-0.707,-0.707),new Vector(0.707,0.707)})
        {
            pet.SetEyeOffsets(GazeMotion.Offset(direction,true,pet.Limits),GazeMotion.Offset(direction,false,pet.Limits));
            var pixels=Pixels(Diagnostics.Render(pet,1254,1254));var outside=0;var changed=0;
            for(var i=0;i<allowed.Length;i++)
            {
                var differs=false;
                for(var c=0;c<4;c++)differs|=Math.Abs(pixels[i*4+c]-center[i*4+c])>1;
                if(differs){changed++;if(!allowed[i])outside++;}
            }
            check(changed>100,$"visible eye motion at {direction.X},{direction.Y}");
            check(outside==0,$"fixed lashes, hair and all pixels outside existing masks unchanged at {direction.X},{direction.Y}");
        }
        pet.SetEyeOffsets(default,default);
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;
    }
    private static void Save(BitmapSource bitmap,string path)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
}
