using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class FrontHairPreview
{
    private static SKBitmap Resolve(SKBitmap source,int size)
    {
        var result=Assets.Empty(size,size);using var canvas=new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(source,new SKRect(0,0,size,size),new SKSamplingOptions(SKFilterMode.Linear));return result;
    }
    internal static void Run(string repository,bool bake)
    {
        var root=Path.Combine(repository,"assets","claude-motion-v2");var output=Path.Combine(repository,"artifacts","front-hair-v2");Directory.CreateDirectory(output);
        using var renderer=new Renderer(root);using var baseline=new Renderer(Path.Combine(repository,"assets","claude-motion-v1"));
        var matchedRoot=Path.Combine(output,"same-face-reference");Directory.CreateDirectory(Path.Combine(matchedRoot,"layers"));
        foreach(var file in Directory.EnumerateFiles(Path.Combine(repository,"assets","claude-motion-v1","layers"),"*.png"))
            File.Copy(file,Path.Combine(matchedRoot,"layers",Path.GetFileName(file)),true);
        File.Copy(Path.Combine(root,"layers","face.png"),Path.Combine(matchedRoot,"layers","face.png"),true);
        File.Copy(Path.Combine(root,"layers","neck.png"),Path.Combine(matchedRoot,"layers","neck.png"),true);
        using var matchedBaseline=new Renderer(matchedRoot);
        var checks=new List<string>();void Check(bool value,string name){if(!value)throw new InvalidDataException(name);checks.Add(name);}
        Check(renderer.HasFrontHair,"renderer selects registered split front layers");
        using var face=Assets.Load(Path.Combine(root,"layers","face.png"));
        var faceInside=FrontHairBuild.InteriorDistance(face,128);var rows=new List<(int Y,int Left,int Right)>();
        for(var y=300;y<=1000;y++)
        {
            var xs=Enumerable.Range(0,1254).Where(x=>face.GetPixel(x,y).Alpha>=128).ToArray();
            Check(xs.Length>0&&xs.Length==xs[^1]-xs[0]+1,$"egg face row {y}: one continuous filled interval, no hair-shaped cutout");
            rows.Add((y,xs[0],xs[^1]));
        }
        Check(rows.Where(r=>r.Y>=400&&r.Y<=980).Max(r=>Math.Abs((r.Left+r.Right)/2f-625))<14,"egg face sides remain balanced around the original face center");
        var maxStep=rows.Zip(rows.Skip(1)).Where(p=>p.First.Y>=400&&p.Second.Y<=980).Max(p=>Math.Max(Math.Abs(p.First.Left-p.Second.Left),Math.Abs(p.First.Right-p.Second.Right)));
        Check(maxStep<=3,"face side contour has no row-to-row horizontal notch/ledge through the cheek and temple region");
        Check(Enumerable.Range(0,1254*1254).Where(p=>faceInside[p]>=3).All(p=>face.GetPixel(p%1254,p/1254).Alpha==255),"egg face interior is fully opaque, not a translucent fill over hair backing");
        foreach(var mesh in renderer.FrontMeshes)
        {
            Check(mesh.Weights.All(w=>w.Length<=4&&w.All(i=>i.Weight>=0)&&Math.Abs(w.Sum(i=>i.Weight)-1)<.00001),mesh.Name+": normalized nonnegative four-weight skinning");
            renderer.Rig.Update(new Pose(),0,0);var bound=mesh.Deform(renderer.Rig);
            Check(bound.Zip(mesh.Rest).All(p=>Vector2.Distance(new Vector2(p.First.X,p.First.Y),p.Second)<.001),mesh.Name+": exact bind pose");
        }
        foreach(var side in new[]{0,1,2})
        {
            var pose=side switch{0=>new FrontHairPose(2,2,0,0,0,0),1=>new(0,0,1.3f,1.3f,0,0),_=>new(0,0,0,0,2,2)};
            renderer.Rig.Update(new Pose(),0,0,pose);
            for(var target=0;target<3;target++)
            {
                var moved=renderer.FrontMeshes[target].Deform(renderer.Rig);
                var max=moved.Zip(renderer.FrontMeshes[target].Rest).Max(p=>Vector2.Distance(new Vector2(p.First.X,p.First.Y),p.Second));
                Check(target==side?max>5:max<.001,$"independent drive {side} -> mesh {target}: {(target==side?"moves":"unchanged")}");
            }
        }
        var differences=new List<object>();
        foreach(var size in new[]{200,280,336,512})
        {
            using var oldHigh=baseline.Render(new Pose(),0,0,size*2);using var newHigh=renderer.Render(new Pose(),0,0,size*2);
            using var sameFaceHigh=matchedBaseline.Render(new Pose(),0,0,size*2);using var sameFaceImage=Resolve(sameFaceHigh,size);
            using var oldImage=Resolve(oldHigh,size);using var newImage=Resolve(newHigh,size);double sum=0;var changed=0;var max=0;double hairError=0;var hairChanged=0;
            using var diff=Assets.Empty(size,size);
            for(var y=0;y<size;y++)for(var x=0;x<size;x++)
            {
                var a=oldImage.GetPixel(x,y);var b=newImage.GetPixel(x,y);
                var d=new[]{Math.Abs(a.Red-b.Red),Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue),Math.Abs(a.Alpha-b.Alpha)}.Max();
                max=Math.Max(max,d);sum+=d;if(d>2){changed++;diff.SetPixel(x,y,new SKColor(255,50,110));}
                var s=sameFaceImage.GetPixel(x,y);
                var hd=new[]{Math.Abs(s.Red-b.Red),Math.Abs(s.Green-b.Green),Math.Abs(s.Blue-b.Blue),Math.Abs(s.Alpha-b.Alpha)}.Max();
                hairError+=hd;if(hd>2)hairChanged++;
            }
            Check(hairError/(size*size)<.05&&hairChanged<size*size*.002,$"same-face old-vs-split hair rendering remains equivalent within subpixel sampling tolerance at {size}px");
            differences.Add(new{Size=size,ChangedPixelsOver2=changed,MaxDifference=max,MeanMaximumChannelError=sum/(size*size),
                SameFaceHairMeanError=hairError/(size*size),SameFaceHairChangedPixelsOver2=hairChanged});
            Assets.Save(oldImage,Path.Combine(output,$"neutral-old-{size}.png"));Assets.Save(newImage,Path.Combine(output,$"neutral-new-{size}.png"));Assets.Save(diff,Path.Combine(output,$"neutral-diff-{size}.png"));
        }
        var motionRecords=new List<object>();
        using var sourceHair=Assets.Load(Path.Combine(repository,"assets","claude-motion-v1","layers","hair.png"));
        var rootInterior=FrontHairBuild.InteriorDistance(sourceHair);var rootPoints=new List<Vector2>();
        for(var y=275;y<=380;y+=3)for(var x=185;x<=1100;x+=3)if(rootInterior[y*1254+x]>10)rootPoints.Add(new(x,y));
        foreach(var id in new[]{"left-only","center-only","right-only","wind","idle","thinking","shake","pat"})
        {
            var dynamics=new FrontHairDynamics();var spring=new Spring(38,10);var tipSpring=new Spring(25,8);
            (Pose Pose,FrontHairPose Front,float Sway,float Tip) Step(double t)
            {
                var authored=id.EndsWith("-only",StringComparison.Ordinal)||id=="wind";
                var pose=authored?new Pose():MotionLayers.Evaluate(id,t,t);
                var force=authored?MotionLayers.Ambient(t)*(id=="wind"?1:1.5f):pose.Wind+pose.AmbientWind-pose.HeadAngle*.85f-pose.HeadX*.05f;
                var front=dynamics.Step(force,1/120f);
                if(id=="left-only")front=new(front.Left,front.LeftTip,0,0,0,0);
                if(id=="center-only")front=new(0,0,front.Center,front.CenterTip,0,0);
                if(id=="right-only")front=new(0,0,0,0,front.Right,front.RightTip);
                var sway=authored?0:spring.Step(force,1/120f);var tip=authored?0:tipSpring.Step(sway,1/120f);
                return(pose,front,sway,tip);
            }
            for(var cycle=0;cycle<4;cycle++)for(var i=0;i<720;i++)Step(i/120d);
            var flips=0;var rootHoles=0;var faceHoles=0;var outOfBounds=0;var handsChanged=0;SKColor[]? initialHands=null;var angles=new List<float[]>();
            for(var frame=0;frame<180;frame++)
            {
                var state=Step(frame/30d);for(var sub=1;sub<4;sub++)state=Step(frame/30d+sub/120d);
                using var high=renderer.Render(state.Pose,state.Sway,state.Tip,672,front:state.Front);using var image=Resolve(high,336);
                if(bake)Assets.Save(image,Path.Combine(output,"frames",id,$"{frame:D3}.png"));
                if(frame==45||frame==135)Assets.Save(image,Path.Combine(output,$"{id}-{frame:D3}.png"));
                renderer.Rig.Update(state.Pose,state.Sway,state.Tip,state.Front);
                foreach(var mesh in renderer.FrontMeshes)
                {
                    var positions=mesh.Deform(renderer.Rig);
                    for(var j=0;j<mesh.Indices.Length;j+=3)
                    {
                        var a=positions[mesh.Indices[j]];var b=positions[mesh.Indices[j+1]];var c=positions[mesh.Indices[j+2]];
                        if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<=0)flips++;
                    }
                }
                SKColor Source(float x,float y,bool head=true)
                {
                    var p=head?Vector2.Transform(new Vector2(x,y),renderer.Rig.Skin[1]):new Vector2(x,y);
                    return high.GetPixel((int)((p.X+73)*672/1400),(int)((p.Y+73)*672/1400));
                }
                foreach(var p in rootPoints)if(Source(p.X,p.Y).Alpha<245)rootHoles++;
                foreach(var p in new[]{(450,850),(790,850),(625,940),(625,400)})if(Source(p.Item1,p.Item2).Alpha<245)faceHoles++;
                var hands=new[]{Source(315,1180,false),Source(930,1180,false)};initialHands??=hands;if(!hands.SequenceEqual(initialHands))handsChanged++;
                for(var j=0;j<336;j++)if(image.GetPixel(j,0).Alpha>32||image.GetPixel(j,335).Alpha>32||image.GetPixel(0,j).Alpha>32||image.GetPixel(335,j).Alpha>32)outOfBounds++;
                angles.Add([state.Front.Left,state.Front.Center,state.Front.Right]);
            }
            Check(flips==0,id+": no inverted front/back triangles in 180 frames");
            Check(rootHoles==0,id+$": no exposed transparency at {rootPoints.Count} interior root/cap points per frame");
            Check(faceHoles==0,id+": completed face interior remains opaque at four sampled points per frame");
            Check(handsChanged==0,id+": both hand sample points stay fixed");Check(outOfBounds==0,id+": no opaque pixels at preview canvas edge");
            motionRecords.Add(new{Id=id,Frames=180,Seconds=6,Fps=30,RootHoles=rootHoles,FaceHoles=faceHoles,Flipped=flips,HandsChanged=handsChanged,EdgeViolations=outOfBounds,Angles=angles});
            Console.WriteLine($"Verified {id}");
        }
        var extremeFlips=0;var extremeHoles=0;
        foreach(var head in new[]{-4f,0,4})for(var bits=0;bits<64;bits++)
        {
            float Sign(int n)=>((bits>>n)&1)==0?-1:1;
            var front=new FrontHairPose(2.5f*Sign(0),2.5f*Sign(1),1.4f*Sign(2),1.4f*Sign(3),2.2f*Sign(4),2.2f*Sign(5));
            var pose=new Pose{HeadAngle=head};renderer.Rig.Update(pose,0,0,front);
            foreach(var mesh in renderer.FrontMeshes)
            {
                var points=mesh.Deform(renderer.Rig);
                for(var i=0;i<mesh.Indices.Length;i+=3)
                {
                    var a=points[mesh.Indices[i]];var b=points[mesh.Indices[i+1]];var c=points[mesh.Indices[i+2]];
                    if((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X)<=0)extremeFlips++;
                }
            }
            using var high=renderer.Render(pose,0,0,672,front:front);
            foreach(var point in rootPoints)
            {
                var p=Vector2.Transform(point,renderer.Rig.Skin[1]);
                if(high.GetPixel((int)((p.X+73)*672/1400),(int)((p.Y+73)*672/1400)).Alpha<245)extremeHoles++;
            }
            if(bits is 0 or 63&&head==0){using var small=Resolve(high,336);Assets.Save(small,Path.Combine(output,$"extreme-{bits}.png"));}
        }
        Check(extremeFlips==0,"all 192 combinations of clamped front angles and +/-4 degree head tilt have non-inverted triangles");
        Check(extremeHoles==0,"all 192 extreme combinations retain dense root/crown opacity");
        var timings=new List<object>();
        foreach(var size in new[]{200,280,400})foreach(var item in new[]{(Name:"previous",Value:baseline),(Name:"split-front",Value:renderer)})
        {
            var width=size+(int)Math.Ceiling(size*73d/1254);
            using var high=Assets.Empty(width*2,width*2);using var highCanvas=new SKCanvas(high);
            using var small=Assets.Empty(width,width);using var smallCanvas=new SKCanvas(small);var ms=new List<double>();
            for(var i=0;i<28;i++)
            {
                var sw=Stopwatch.StartNew();item.Value.Draw(highCanvas,new Pose{HeadAngle=.8f},.5f,.3f,size*2/1254f,79,83,front:new FrontHairPose(.8f,.5f,.4f,.2f,.6f,.4f));
                smallCanvas.Clear(SKColors.Transparent);smallCanvas.DrawBitmap(high,new SKRect(0,0,width,width),new SKSamplingOptions(SKFilterMode.Linear));
                sw.Stop();if(i>=6)ms.Add(sw.Elapsed.TotalMilliseconds);
            }
            ms.Sort();timings.Add(new{Pixels=size,Mode=item.Name,MedianMs=ms[ms.Count/2],P95Ms=ms[(int)(ms.Count*.95)],TextureBytes=item.Value.TextureBytes});
        }
        File.WriteAllText(Path.Combine(root,"rig-v2.json"),JsonSerializer.Serialize(new{Version=2,Bones=renderer.Rig.Bones.Select(b=>new{b.Name,b.Parent,Rest=new[]{b.Rest.X,b.Rest.Y}}),
            Meshes=renderer.FrontMeshes.Select(m=>new{m.Name,Rest=m.Rest.Select(p=>new[]{p.X,p.Y}),m.Weights,m.Indices}),
            FixedHz=120,RootPinnedThroughY=365,IndependentSprings=new[]{new[]{36,9,22,7},new[]{53,12,34,9},new[]{43,10,27,8}},
            WeightPolicy="head root -> per-part mid -> per-part tip; side bottom fades to body root",Rendering="2x CPU high-quality sampling; premultiplied-alpha 2:1 resolve"},Assets.Json));
        File.WriteAllText(Path.Combine(root,"deformation-report.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,
            NeutralSampling=differences,Motions=motionRecords,TextureBytes=renderer.TextureBytes,Timings=timings,ExtremeCases=192,
            Scope="Offscreen rig/render validation only. Sampled seam/face/hand checks do not prove every arbitrary pose. Final isolated-layer and dynamic visual review still required. No installed plugin changed."},Assets.Json));
    }
}
