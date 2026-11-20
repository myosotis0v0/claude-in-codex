using System.Numerics;
using SkiaSharp;

namespace MotionLab;

internal sealed record Bone(string Name,int Parent,Vector2 Rest);
internal sealed record Influence(int Bone,float Weight);

internal sealed class Rig
{
    internal Bone[] Bones {get;}
    internal Matrix3x2[] Skin {get;}
    internal WeightBand RearBand {get;}
    private readonly Matrix3x2[] world;
    internal Rig(CharacterRig profile)
    {
        Bones=profile.CreateBones();Skin=new Matrix3x2[Bones.Length];world=new Matrix3x2[Bones.Length];RearBand=profile.Weights.RearFlow;
    }
    internal float RearOffset {get;private set;}
    internal void Update(Pose pose,float sway,float tip,FrontHairPose front=default)
    {
        RearOffset=RearHairFlow.Offset(sway,tip);
        for(var i=0;i<Bones.Length;i++)
        {
            var bone=Bones[i];var parent=bone.Parent<0?Vector2.Zero:Bones[bone.Parent].Rest;
            var angle=i switch{1=>pose.HeadAngle,2=>sway*.55f,3=>sway*.35f,4=>sway*.5f,5=>sway*.65f,6=>tip*.75f,7=>sway*.55f,8=>tip*.7f,9=>sway*.7f,10=>tip*.9f,11=>sway*.12f,
                13=>front.Left,14=>front.LeftTip*.5f,15=>front.Center,16=>front.CenterTip*.5f,17=>front.Right,18=>front.RightTip*.5f,_=>0};
            var translation=bone.Rest-parent+(i==1?new Vector2(pose.HeadX,pose.HeadY):Vector2.Zero);
            var scale=i==12?Matrix3x2.CreateScale(1,1+pose.Breath):Matrix3x2.Identity;
            var local=scale*Matrix3x2.CreateRotation(angle*MathF.PI/180)*Matrix3x2.CreateTranslation(translation);
            world[i]=bone.Parent<0?local:local*world[bone.Parent];Skin[i]=Matrix3x2.CreateTranslation(-bone.Rest)*world[i];
        }
    }
    internal static SKMatrix ToSkia(Matrix3x2 m)=>new(){ScaleX=m.M11,SkewX=m.M21,TransX=m.M31,SkewY=m.M12,ScaleY=m.M22,TransY=m.M32,Persp2=1};
}

internal sealed class Mesh:IDisposable
{
    private SKPoint[] points=[],uv=[];
    private SKPaint? cachedPaint;
    private SKShader? cachedShader;
    private ushort[] visibleIndices=[];
    internal Vector2[] Rest {get;}
    internal Influence[][] Weights {get;}
    internal ushort[] Indices {get;}
    internal string Name {get;}
    internal bool BottomOverscan {get;}
    private readonly MeshSpec spec;
    private readonly WeightSettings profile;
    private readonly bool touchTips;
    private bool rearFlow;
    internal bool RearFlow
    {
        get=>rearFlow;
        set
        {
            rearFlow=value;
            for(var i=0;i<Rest.Length;i++)
            {
                Weights[i]=value?[new(1,1)]:Assign(Rest[i]);
            }
        }
    }
    internal Mesh(CharacterRig rig,string name,bool bottomOverscan=false,bool touchTips=false)
    {
        Name=name;BottomOverscan=bottomOverscan;spec=rig.Meshes[name];profile=rig.Weights;this.touchTips=touchTips;
        var (columns,rows)=(spec.Columns,spec.Rows);
        var ys=Enumerable.Range(0,rows+1).Select(y=>y*1254f/rows).ToList();
        if(bottomOverscan){ys.Add(RearHairFlow.ExtensionStart);ys.Add(RearHairFlow.ExtensionBottom);ys=ys.Distinct().Order().ToList();}
        Rest=new Vector2[(columns+1)*ys.Count];Weights=new Influence[Rest.Length][];var triangles=new List<ushort>();
        for(var y=0;y<ys.Count;y++)for(var x=0;x<=columns;x++)
        {
            var i=y*(columns+1)+x;var point=new Vector2(x*1254f/columns,ys[y]);Rest[i]=point;Weights[i]=Assign(point);
            if(x<columns&&y<ys.Count-1)
            {
                var b=i+columns+1;triangles.AddRange([(ushort)i,(ushort)(i+1),(ushort)b,(ushort)(i+1),(ushort)(b+1),(ushort)b]);
            }
        }
        Indices=triangles.ToArray();
    }
    private Influence[] Assign(Vector2 p)
    {
        if(touchTips)
        {
            var tip=profile.TouchTip.Smooth(p.Y);return [new(1,1-tip),new(spec.Tip,tip)];
        }
        if(spec.Weights=="frontHair")
        {
            var f=profile.FrontHair;
            var bend=f.Bend.Smooth(p.Y);var tip=f.Tip.Smooth(p.Y);var body=spec.BodyBlend?f.Body.Smooth(p.Y):0;
            return [new(1,(1-bend)*(1-body)),new(spec.Mid,bend*(1-tip)*(1-body)),new(spec.Tip,bend*tip*(1-body)),new(0,body)];
        }
        if(spec.Weights=="torso")
        {
            var w=Math.Clamp((1-MathF.Abs(p.X-profile.Torso.X)/profile.Torso.HalfWidth),0,1);
            return [new(0,1-w),new(12,w)];
        }
        if(spec.Weights=="accessory")
        {
            var t=profile.Accessory.Linear(p.Y);
            return t<=0?[new(11,1)]:[new(9,1-t),new(10,t)];
        }
        var hair=profile.LegacyHair;
        if(spec.Weights=="head"||p.Y<hair.HeadOnlyAbove)return [new(1,1)];
        static float G(Vector2 p,WeightField f)=>MathF.Exp(-.5f*(MathF.Pow((p.X-f.X)/f.Width,2)+MathF.Pow((p.Y-f.Y)/f.Height,2)));
        var bottom=hair.Body.Linear(p.Y);
        var values=new Influence[hair.Fields.Length+2];
        values[0]=new(0,bottom*hair.BodyGain);values[1]=new(1,hair.HeadGain*(1-bottom));
        for(int i=0;i<hair.Fields.Length;i++){var f=hair.Fields[i];values[i+2]=new(f.Bone,G(p,f)*f.Gain);}
        var top=values.OrderByDescending(w=>w.Weight).Take(hair.MaxInfluences).ToArray();var sum=top.Sum(w=>w.Weight);
        return top.Select(w=>w with{Weight=w.Weight/sum}).ToArray();
    }
    internal SKPoint[] Deform(Rig rig)
    {
        var result=new SKPoint[Rest.Length];
        for(var i=0;i<Rest.Length;i++)
        {
            var point=DeformPoint(rig,i);
            result[i]=new(point.X,point.Y);
        }
        return result;
    }
    private Vector2 DeformPoint(Rig rig,int index)
    {
        if(RearFlow)return RearHairFlow.Deform(Rest[index],rig);
        var point=Vector2.Zero;foreach(var weight in Weights[index])point+=Vector2.Transform(Rest[index],rig.Skin[weight.Bone])*weight.Weight;
        return point;
    }
    internal void Draw(SKCanvas canvas,Rig rig,SKBitmap texture,int originX=0,int originY=0,bool highQuality=true,Func<Vector2,Vector2>? offset=null)
    {
        PrepareTexture(texture,originX,originY,highQuality);
        for(var i=0;i<Rest.Length;i++)
        {
            var point=DeformPoint(rig,i);
            if(offset!=null)point+=offset(Rest[i]);
            points[i]=new(point.X,point.Y);
        }
        using var vertices=SKVertices.CreateCopy(SKVertexMode.Triangles,points,uv,null,visibleIndices);
        canvas.DrawVertices(vertices,SKBlendMode.Modulate,cachedPaint!);
    }
    internal void PrepareTexture(SKBitmap texture,int originX,int originY,bool highQuality)
    {
        if(cachedPaint!=null)return;
        points=new SKPoint[Rest.Length];uv=Rest.Select(p=>new SKPoint(p.X-originX,p.Y-originY)).ToArray();
        cachedShader=texture.ToShader(SKShaderTileMode.Clamp,SKShaderTileMode.Clamp,highQuality?new SKSamplingOptions(SKCubicResampler.Mitchell):new SKSamplingOptions(SKFilterMode.Linear));
        cachedPaint=new SKPaint{IsAntialias=true,Shader=cachedShader};
        // Cull source-space transparent triangles once, never by their deformed screen bounds.
        var stride=texture.Width+1;var sums=new int[stride*(texture.Height+1)];
        for(var y=0;y<texture.Height;y++)for(var x=0;x<texture.Width;x++)
            sums[(y+1)*stride+x+1]=(texture.GetPixel(x,y).Alpha>0?1:0)+sums[y*stride+x+1]+sums[(y+1)*stride+x]-sums[y*stride+x];
        var visible=new List<ushort>();
        for(var i=0;i<Indices.Length;i+=3)
        {
            var a=uv[Indices[i]];var b=uv[Indices[i+1]];var c=uv[Indices[i+2]];
            var x1=Math.Clamp((int)Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X)))-2,0,texture.Width);
            var y1=Math.Clamp((int)Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y)))-2,0,texture.Height);
            var x2=Math.Clamp((int)Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X)))+2,0,texture.Width);
            var y2=Math.Clamp((int)Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y)))+2,0,texture.Height);
            if(sums[y2*stride+x2]-sums[y1*stride+x2]-sums[y2*stride+x1]+sums[y1*stride+x1]>0)
                visible.AddRange([Indices[i],Indices[i+1],Indices[i+2]]);
        }
        visibleIndices=visible.ToArray();
    }
    public void Dispose(){cachedPaint?.Dispose();cachedShader?.Dispose();}
}

internal sealed class Spring(float stiffness,float damping)
{
    internal float Position {get;private set;}
    private float velocity;
    internal float Step(float target,float dt)
    {
        velocity+=(stiffness*(target-Position)-damping*velocity)*dt;
        Position=Math.Clamp(Position+velocity*dt,-6.5f,6.5f);return Position;
    }
}
