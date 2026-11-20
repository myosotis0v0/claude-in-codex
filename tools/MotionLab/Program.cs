using MotionLab;
using SkiaSharp;
using System.Text.Json;

if(args.Length<1)throw new ArgumentException("Usage: MotionLab <repo> [prepare|neutral|bake|verify]");
var repository=Path.GetFullPath(args[0]);var command=args.Length>1?args[1]:"bake";
if(command=="rear-hair-complete"){RearHairCompletion.Build(repository);return;}
if(command=="completion-draft"){CompletionDraft.Run(repository);return;}
if(command=="front-hair-inspect"){FrontHairBuild.Inspect(repository);return;}
if(command=="front-hair-build"){FrontHairBuild.Build(repository);return;}
if(command=="menu-hair-build"){MenuHairBuild.Run(repository);return;}
if(command=="front-hair-preview"||command=="front-hair-verify"){FrontHairPreview.Run(repository,command=="front-hair-preview");return;}
if(command=="front-hair-audit"){FrontHairAudit.Run(repository);return;}
if(command=="chin-neck-preview"){ChinNeckRepair.Preview(repository);return;}
if(command=="rear-hair-inspect"){RearHairStudy.Inspect(repository);return;}
if(command=="rear-hair-split"){RearHairSplit.Build(repository);return;}
if(command=="rear-hair-preview"){RearHairStudy.Run(repository);return;}
if(command=="rear-hair-attachment-inspect"){RearHairAttachmentStudy.Inspect(repository);return;}
if(command=="skinning-preview"){SkinningPreview.Run(repository);return;}
if(command=="right-cheek-cleanup"){RightCheekCleanup.Build(repository);return;}
if(command=="rear-hair-attachment-before"||command=="rear-hair-attachment-preview"){RearHairAttachmentStudy.Run(repository,command=="rear-hair-attachment-before");return;}
var root=Path.Combine(repository,"assets","claude-motion-v1");var output=Path.Combine(repository,"artifacts","motion-batch-v1");Directory.CreateDirectory(output);
if(command=="prepare"){Assets.Prepare(repository,root);return;}
using var renderer=new Renderer(root);
if(command=="neutral")
{
    using var neutral=renderer.Render(new Pose(),0,0,512);Assets.Save(neutral,Path.Combine(output,"neutral.png"));
    using var debug=renderer.Render(new Pose(),0,0,512,true);Assets.Save(debug,Path.Combine(output,"rig-debug.png"));return;
}
if(command=="poses")
{
    foreach(var motion in Motions.Catalog)
    {
        using var frame=renderer.Render(Motions.Evaluate(motion.Id,2.8),0,0,384);Assets.Save(frame,Path.Combine(output,$"pose-{motion.Id}.png"));
    }
    return;
}
if(command=="layer-demo"){LayerDemo.Run(renderer,Path.Combine(repository,"artifacts","chin-wind-fix"));return;}
if(command!="bake"&&command!="verify")throw new ArgumentException(command);
var checks=new List<string>();
void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
foreach(var mesh in renderer.Meshes)
{
    Check(mesh.Weights.All(ws=>ws.Length<=4&&Math.Abs(ws.Sum(w=>w.Weight)-1)<.00001),$"{mesh.Name}: max four normalized bone weights per vertex");
    renderer.Rig.Update(new Pose(),0,0);var neutral=mesh.Deform(renderer.Rig);
    Check(neutral.Zip(mesh.Rest).All(pair=>Math.Abs(pair.First.X-pair.Second.X)<.001&&Math.Abs(pair.First.Y-pair.Second.Y)<.001),$"{mesh.Name}: bind pose reconstructs rest vertices");
}
foreach(var seconds in new[]{0d,.25,1.5,4.2,5.9})Check(Math.Abs(MotionLayers.Ambient(seconds)-MotionLayers.Ambient(seconds+6))<.00001,$"ambient wind repeats continuously at {seconds}s");
foreach(var id in new[]{"idle","thinking","talk","shy"})
    Check(MotionLayers.Evaluate(id,.2,1.5).AmbientWind==MotionLayers.Evaluate(id,4.5,1.5).AmbientWind,$"{id}: ambient phase depends on global time, not local expression time");
foreach(var id in new[]{"breeze","shake","nod","pat"})
{
    var resolved=MotionLayers.Evaluate(id,2.8,1.5);var authored=Motions.Evaluate(id,2.8);
    Check(resolved.AmbientWind==0&&resolved.Wind==authored.Wind,$"{id}: full authored hair gesture suppresses ambient without overwriting gesture");
}
var rigDocument=new{Version=1,Renderer="SkiaSharp 4.156.1, offline authoring only",BoneConvention="row-vector local * parent; inverse bind * animated world",Bones=renderer.Rig.Bones.Select(b=>new{b.Name,b.Parent,Rest=new[]{b.Rest.X,b.Rest.Y}}),
    Meshes=renderer.Meshes.Select(mesh=>new{mesh.Name,Vertices=mesh.Rest.Select(p=>new[]{p.X,p.Y}),mesh.Weights,mesh.Indices}),
    SecondaryMotion=new{FixedHz=120,First=new[]{38,10},Tip=new[]{25,8},MaxDegrees=6.5,WarmupCycles=5},
    RuntimeDependenciesAdded=Array.Empty<string>(),Sources="generated PNGs plus source-preserving extraction; no bundled Cubism Core"};
File.WriteAllText(Path.Combine(root,"rig.json"),JsonSerializer.Serialize(rigDocument,Assets.Json));
File.WriteAllText(Path.Combine(root,"motions.json"),JsonSerializer.Serialize(new{Version=2,Motions.Fps,Motions.Duration,Catalog=Motions.Catalog,
    AmbientLayer=new{Enabled=true,AmplitudeDegrees=MotionLayers.AmbientAmplitude,PeriodSeconds=6,Clock="persistent global active time; do not restart on expression/state change",Policy="add under quiet-hair states; fade out under authored breeze/shake/nod/pat; never overwrite authored Wind"},
    Priority="explicit reaction > expression > quiet idle; touch/drag interruptions are future host wiring, not installed listeners"},Assets.Json));
var results=new List<object>();
foreach(var motion in Motions.Catalog)
{
    var directory=Path.Combine(output,motion.Id);Directory.CreateDirectory(directory);
    var spring=new Spring(38,10);var tip=new Spring(25,8);
    (Pose Pose,float Sway,float Tip) Tick(double seconds)
    {
        var pose=MotionLayers.Evaluate(motion.Id,seconds,seconds);var target=pose.Wind+pose.AmbientWind-pose.HeadAngle*.85f-pose.HeadX*.05f;
        var first=spring.Step(target,1/120f);var second=tip.Step(first,1/120f);return(pose,first,second);
    }
    for(var cycle=0;cycle<5;cycle++)for(var sub=0;sub<720;sub++)Tick(sub/120d);
    var firstSway=spring.Position;var firstTip=tip.Position;var transparencyFailures=0;var flipped=0;var boundsFailures=0;var chinHoles=0;
    var handFailures=0;SKColor[]? initialHands=null;
    var poseFrames=new List<object>();
    for(var frame=0;frame<Motions.Frames;frame++)
    {
        var state=Tick(frame/30d);for(var sub=1;sub<4;sub++)state=Tick(frame/30d+sub/120d);
        using var bitmap=renderer.Render(state.Pose,state.Sway,state.Tip);
        if(command=="bake")Assets.Save(bitmap,Path.Combine(directory,$"{frame:D3}.png"));
        if(command=="bake"&&motion.Id=="breeze")
        {
            using var debug=renderer.Render(state.Pose,state.Sway,state.Tip,336,true);Assets.Save(debug,Path.Combine(output,"rig-debug",$"{frame:D3}.png"));
        }
        var hands=new[]{(315,1180),(930,1180)}.Select(p=>bitmap.GetPixel((int)((p.Item1+73)*bitmap.Width/1400f),(int)((p.Item2+73)*bitmap.Height/1400f))).ToArray();
        initialHands??=hands;if(!hands.SequenceEqual(initialHands))handFailures++;
        renderer.Rig.Update(state.Pose,state.Sway,state.Tip);
        foreach(var mesh in renderer.Meshes)
        {
            var points=mesh.Deform(renderer.Rig);
            for(var i=0;i<mesh.Indices.Length;i+=3)
            {
                var a=points[mesh.Indices[i]];var b=points[mesh.Indices[i+1]];var c=points[mesh.Indices[i+2]];
                if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<=0)flipped++;
            }
            if(points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)))throw new InvalidDataException("Nonfinite mesh vertex");
        }
        for(var i=0;i<bitmap.Width;i++)if(bitmap.GetPixel(i,0).Alpha>32||bitmap.GetPixel(i,bitmap.Height-1).Alpha>32||bitmap.GetPixel(0,i).Alpha>32||bitmap.GetPixel(bitmap.Width-1,i).Alpha>32)boundsFailures++;
        // Interior face samples must never expose transparency when hair moves.
        foreach(var point in new[]{new System.Numerics.Vector2(450,850),new(790,850),new(625,940)})
        {
            var moved=System.Numerics.Vector2.Transform(point,renderer.Rig.Skin[1]);var px=(int)((moved.X+73)*bitmap.Width/1400);var py=(int)((moved.Y+73)*bitmap.Height/1400);
            if(bitmap.GetPixel(px,py).Alpha<245)transparencyFailures++;
        }
        foreach(var x in new[]{430,475,525,575,625,675,725,775,820})foreach(var y in new[]{1045,1055,1065,1080})
        {
            var moved=System.Numerics.Vector2.Transform(new System.Numerics.Vector2(x,y),renderer.Rig.Skin[1]);
            if(bitmap.GetPixel((int)((moved.X+73)*bitmap.Width/1400),(int)((moved.Y+73)*bitmap.Height/1400)).Alpha<245)chinHoles++;
        }
        poseFrames.Add(new{Frame=frame,state.Pose,Sway=state.Sway,Tip=state.Tip});
    }
    Check(flipped==0,$"{motion.Id}: no flipped skinning triangles in 180 frames");
    Check(boundsFailures==0,$"{motion.Id}: silhouette remains inside fixed frame");
    Check(transparencyFailures==0,$"{motion.Id}: face under moving bangs never exposes transparency at sampled interiors");
    Check(handFailures==0,$"{motion.Id}: opaque resting-paw samples remain pinned across the clip");
    Check(chinHoles==0,$"{motion.Id}: 36 under-chin corridor samples stay filled throughout the clip");
    Check(Math.Abs(firstSway-spring.Position)<.001&&Math.Abs(firstTip-tip.Position)<.001,$"{motion.Id}: periodic secondary motion joins continuously");
    var file=Path.Combine(root,$"motion-{motion.Id}.json");File.WriteAllText(file,JsonSerializer.Serialize(new{motion.Id,Motions.Fps,Motions.Duration,Frames=poseFrames},Assets.Json));
    results.Add(new{motion.Id,motion.Label,Frames=Motions.Frames,FlippedTriangles=flipped,BoundaryPixels=boundsFailures,FaceHoles=transparencyFailures,ChinHoles=chinHoles});Console.WriteLine($"Baked {motion.Id}: {Motions.Frames} frames");
}
var runtimeFiles=new[]{"parts.json","rig.json","motions.json"}.Select(p=>Path.Combine(root,p))
    .Concat(Directory.EnumerateFiles(root,"motion-*.json")).Concat(Directory.EnumerateFiles(Path.Combine(root,"layers"),"*.png")).ToArray();
var runtimeBytes=runtimeFiles.Sum(p=>new FileInfo(p).Length);
File.WriteAllText(Path.Combine(root,"payload-manifest.json"),JsonSerializer.Serialize(new{Version=1,Bytes=runtimeBytes,
    Files=runtimeFiles.Select(path=>new{Path=Path.GetRelativePath(root,path).Replace('\\','/'),Bytes=new FileInfo(path).Length,
        Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}),
    Excluded=new[]{"generated/","previews/","offline Skia binaries","Cubism Core"}},Assets.Json));
Check(runtimeBytes<12_000_000,"layer/motion candidate payload under 12 MB, without offline Skia binaries or previews");
File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Clips=results,RuntimeCandidateBytes=runtimeBytes,
    Scope="Offline source assets and Skia rendering only. No installed plugin, host window, mouse, keyboard or settings were touched."},Assets.Json));
Console.WriteLine($"Checks passed: {checks.Count}; candidate bytes: {runtimeBytes}");
