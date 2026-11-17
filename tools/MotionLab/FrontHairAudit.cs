using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class FrontHairAudit
{
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private sealed record Component(int Pixels,int Left,int Top,int Right,int Bottom);
    private static Component[] Components(SKBitmap image)
    {
        var w=image.Width;var h=image.Height;var mask=new bool[w*h];var queue=new Queue<int>();var sizes=new List<Component>();
        for(var y=0;y<h;y++)for(var x=0;x<w;x++)mask[y*w+x]=image.GetPixel(x,y).Alpha>=128;
        for(var p=0;p<mask.Length;p++)if(mask[p])
        {
            var count=0;var l=w;var t=h;var r=0;var bottom=0;mask[p]=false;queue.Enqueue(p);
            while(queue.TryDequeue(out var at))
            {
                count++;var x=at%w;var y=at/w;l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);bottom=Math.Max(bottom,y+1);
                void Visit(int next){if(mask[next]){mask[next]=false;queue.Enqueue(next);}}
                if(x>0)Visit(at-1);if(x<w-1)Visit(at+1);if(y>0)Visit(at-w);if(y<h-1)Visit(at+w);
            }
            sizes.Add(new(count,l,t,r,bottom));
        }
        return sizes.OrderByDescending(c=>c.Pixels).ToArray();
    }
    internal static List<object> CheckComponents(string root)
    {
        var components=new List<object>();var detached=new List<string>();
        foreach(var name in new[]{"face","hair-front-left","hair-front-center","hair-front-right"})
        {
            using var image=Assets.Load(Path.Combine(root,"layers",name+".png"));var sizes=Components(image);
            if(sizes.Length==0||sizes[0].Pixels<=10000)throw new InvalidDataException(name+": incomplete main silhouette");
            if(sizes.Skip(1).Any(n=>n.Pixels>4))detached.Add(name+": "+JsonSerializer.Serialize(sizes.Skip(1).Where(c=>c.Pixels>4)));
            components.Add(new{Name=name,Components=sizes});
        }
        File.WriteAllText(Path.Combine(root,"component-inspection.json"),JsonSerializer.Serialize(components,Assets.Json));
        if(detached.Count>0)throw new InvalidDataException("detached opaque foreign-fringe fragments: "+string.Join("; ",detached));
        return components;
    }
    internal static void Run(string repository)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v2");var layers=Path.Combine(root,"layers");
        var output=Path.Combine(repository,"artifacts","front-hair-v2");var previews=Path.Combine(repository,"previews","front-hair-v2");
        var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new InvalidDataException(name);checks.Add(name);}
        using var registration=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"registration-report.json")));
        using var deformation=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"deformation-report.json")));
        Check(registration.RootElement.GetProperty("GatePassed").GetBoolean(),"native rest-pose hair reassembly gate passed with the same corrected face on both sides");
        Check(deformation.RootElement.GetProperty("Success").GetBoolean(),"deformation validator completed all motions and extremes");
        var newest=Directory.EnumerateFiles(layers,"*.png").Max(File.GetLastWriteTimeUtc);
        Check(File.GetLastWriteTimeUtc(Path.Combine(root,"deformation-report.json"))>=newest,"deformation report is newer than every accepted layer");
        Check(Hash(Path.Combine(output,"reference-flat.png"))==Hash(Path.Combine(output,"joined-flat.png")),"encoded reference and reassembled native flat PNGs are identical");
        using(var oldFace=Assets.Load(Path.Combine(repository,"assets","claude-motion-v1","layers","face.png")))
        using(var face=Assets.Load(Path.Combine(layers,"face.png")))
        {
            var mismatch=0;for(var y=950;y<1254;y++)for(var x=0;x<1254;x++)if(!ChinNeckRepair.ChinPatch(x,y)&&oldFace.GetPixel(x,y)!=face.GetPixel(x,y))mismatch++;
            Check(mismatch==0,"saved production PNG retains the original lower jaw outside the central dent repair");
            using var neck=Assets.Load(Path.Combine(layers,"neck.png"));ChinNeckRepair.Validate(repository,face,oldFace,neck);
            Check(true,"central chin has no inward notch and exposed neck has no dark center seam");
        }
        var components=CheckComponents(root);
        Check(true,"all completed silhouettes have a connected main body and no detached opaque foreign-fringe fragments larger than four source pixels");
        var source=Path.Combine(repository,"assets","claude-motion-v1","layers");var immutable=new Dictionary<string,string>();
        foreach(var file in Directory.EnumerateFiles(source,"*.png").Where(p=>Path.GetFileName(p) is not("face.png" or "hair.png" or "neck.png")))
        {
            var name=Path.GetFileName(file);Check(Hash(file)==Hash(Path.Combine(layers,name)),name+": original protected part unchanged");immutable[name]=Hash(file);
        }
        Check(deformation.RootElement.GetProperty("Motions").GetArrayLength()==8,"eight independent/natural/action animation clips validated");
        foreach(var motion in deformation.RootElement.GetProperty("Motions").EnumerateArray())
            Check(motion.GetProperty("Frames").GetInt32()==180&&motion.GetProperty("Fps").GetInt32()==30,"source clip has 180 frames at 30 FPS: "+motion.GetProperty("Id").GetString());
        Check(deformation.RootElement.GetProperty("ExtremeCases").GetInt32()==192,"192 clamped angle/head-tilt extremes checked");
        var candidate=Path.Combine(output,"plugin-candidate","assets","motion","layers");
        foreach(var file in Directory.EnumerateFiles(layers,"*.png"))
            Check(Hash(file)==Hash(Path.Combine(candidate,Path.GetFileName(file))),"candidate uses the accepted exact layer: "+Path.GetFileName(file));
        foreach(var folder in new[]{"wpf-motion-final","wpf-regression-final","wpf-quality-final"})
        {
            var file=Path.Combine(output,folder,folder=="wpf-motion-final"?"motion-runtime-report.json":folder=="wpf-quality-final"?"quality-report.json":"report.json");
            using var report=JsonDocument.Parse(File.ReadAllText(file));
            Check(report.RootElement.GetProperty("Success").GetBoolean()&&File.GetLastWriteTimeUtc(file)>=newest,folder+": final accepted material passes actual WPF offscreen checks");
        }
        using var media=JsonDocument.Parse(File.ReadAllText(Path.Combine(previews,"preview-manifest.json")));
        foreach(var item in media.RootElement.GetProperty("Media").EnumerateArray())
        {
            var name=item.GetProperty("File").GetString()!;
            Check(item.GetProperty("Milliseconds").GetInt32()==6000&&File.GetLastWriteTimeUtc(Path.Combine(previews,name))>=newest,name+": final media has a verified six-second loop and is current");
        }
        var artifacts=new Dictionary<string,string>();
        foreach(var name in new[]{"static-comparison-dark.png","static-comparison-light.png","seam-phase-contact.png","bound-parts.png","front-hair-dynamics.gif","front-hair-production-v2.zip"})
        {
            var path=Path.Combine(previews,name);Check(File.GetLastWriteTimeUtc(path)>=newest,name+": review artifact is current");artifacts[name]=Hash(path);
        }
        var installedRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","plugins","cache","claude-in-codex-local","claude-in-codex","0.5.0");
        var installed=Directory.Exists(installedRoot)&&Directory.EnumerateFiles(layers,"*.png").All(file=>
            File.Exists(Path.Combine(installedRoot,"bin","assets","motion","layers",Path.GetFileName(file)))&&
            Hash(file)==Hash(Path.Combine(installedRoot,"bin","assets","motion","layers",Path.GetFileName(file))));
        if(installed)Check(Hash(Path.Combine(installedRoot,"bin","ClaudeCompanion.dll"))==Hash(Path.Combine(output,"plugin-candidate","ClaudeCompanion.dll")),
            "installed 0.5.0 assembly and layers match the tested candidate; this does not assert the running process version");
        File.WriteAllText(Path.Combine(root,"production-readiness.json"),JsonSerializer.Serialize(new{ReadyForValidatedProfile=true,Checks=checks,
            Profile="2x CPU high-quality sampling; original jaw protected outside central dent repair; front angular clamps and +/-4 degree head tilt; root pinned through y365",
            IndependentParts=components,ProtectedOriginalParts=immutable,ReviewArtifactSha256=artifacts,
            Installed=installed,InstalledVersion=installed?"0.5.0":null,RunningVersionVerified=false,
            Scope="Accepted material/rig/offscreen rendering and encoded media; installed flag verifies package contents only, not live activation. No user desktop controlled, no native host interaction proof, no claim for poses outside the validated profile. Visual phase/static review is recorded in README.md."},Assets.Json));
        Console.WriteLine($"Production profile audit: {checks.Count} checks passed");
    }
}
