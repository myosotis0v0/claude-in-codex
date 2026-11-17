using System.Numerics;
using SkiaSharp;

namespace MotionLab;

internal sealed record Bone(string Name,int Parent,Vector2 Rest);
internal sealed record Influence(int Bone,float Weight);

internal sealed class Rig
{
    internal Bone[] Bones {get;}=[new("root",-1,new(0,0)),new("head",0,new(627,1030)),
        new("bang-left",1,new(395,425)),new("bang-center",1,new(610,380)),new("bang-right",1,new(865,430)),
        new("side-left",1,new(200,650)),new("side-left-tip",5,new(160,950)),
        new("side-right",1,new(1060,640)),new("side-right-tip",7,new(1110,970)),
        new("ribbon-root",1,new(1030,530)),new("ribbon-tip",9,new(1090,830)),
        new("flower",1,new(1014,382)),new("breath",0,new(627,1250)),
        new("front-left-mid",1,new(380,365)),new("front-left-tip",13,new(230,670)),
        new("front-center-mid",1,new(625,365)),new("front-center-tip",15,new(625,650)),
        new("front-right-mid",1,new(855,365)),new("front-right-tip",17,new(1000,670))];
    internal Matrix3x2[] Skin {get;}=new Matrix3x2[19];
    private readonly Matrix3x2[] world=new Matrix3x2[19];
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
    private bool rearFlow;
    internal bool RearFlow
    {
        get=>rearFlow;
        set
        {
            rearFlow=value;
            for(var i=0;i<Rest.Length;i++)
            {
                Weights[i]=value?[new(1,1)]:Assign(Name,Rest[i]);
            }
        }
    }
    internal Mesh(string name,int columns=32,int rows=36)
    {
        Name=name;Rest=new Vector2[(columns+1)*(rows+1)];Weights=new Influence[Rest.Length][];var triangles=new List<ushort>();
        for(var y=0;y<=rows;y++)for(var x=0;x<=columns;x++)
        {
            var i=y*(columns+1)+x;var point=new Vector2(x*1254f/columns,y*1254f/rows);Rest[i]=point;Weights[i]=Assign(name,point);
            if(x<columns&&y<rows)
            {
                var b=i+columns+1;triangles.AddRange([(ushort)i,(ushort)(i+1),(ushort)b,(ushort)(i+1),(ushort)(b+1),(ushort)b]);
            }
        }
        Indices=triangles.ToArray();
    }
    private static Influence[] Assign(string name,Vector2 p)
    {
        if(name.StartsWith("hair-front-",StringComparison.Ordinal))
        {
            var mid=name.EndsWith("left",StringComparison.Ordinal)?13:name.EndsWith("center",StringComparison.Ordinal)?15:17;
            static float Smooth(float v){v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
            var bend=Smooth((p.Y-365)/270);var tip=Smooth((p.Y-610)/260);
            var body=name.EndsWith("center",StringComparison.Ordinal)?0:Smooth((p.Y-990)/170);
            return [new(1,(1-bend)*(1-body)),new(mid,bend*(1-tip)*(1-body)),new(mid+1,bend*tip*(1-body)),new(0,body)];
        }
        if(name=="clothes-paws"||name=="neck")
        {
            var w=Math.Clamp((1-MathF.Abs(p.X-627)/165),0,1);
            return [new(0,1-w),new(12,w)];
        }
        if(name=="accessory")
        {
            var t=Math.Clamp((p.Y-535)/500,0,1);
            return t<=0?[new(11,1)]:[new(9,1-t),new(10,t)];
        }
        if(p.Y<240)return [new(1,1)];
        static float G(Vector2 a,float x,float y,float sx,float sy)=>MathF.Exp(-.5f*(MathF.Pow((a.X-x)/sx,2)+MathF.Pow((a.Y-y)/sy,2)));
        var bottom=Math.Clamp((p.Y-970)/270,0,1);
        var values=new[]{new Influence(0,bottom*8),new Influence(1,1.2f*(1-bottom)),
            new Influence(2,G(p,395,660,105,185)*1.6f),new Influence(3,G(p,610,655,110,185)*1.5f),new Influence(4,G(p,850,640,115,195)*1.4f),
            new Influence(5,G(p,160,780,160,210)*2),new Influence(6,G(p,170,1070,160,180)*2),
            new Influence(7,G(p,1080,780,155,210)*2),new Influence(8,G(p,1110,1080,145,180)*2)};
        var top=values.OrderByDescending(w=>w.Weight).Take(4).ToArray();var sum=top.Sum(w=>w.Weight);
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
    internal void Draw(SKCanvas canvas,Rig rig,SKBitmap texture,int originX=0,int originY=0,bool highQuality=true)
    {
        if(cachedPaint==null)
        {
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
        for(var i=0;i<Rest.Length;i++)
        {
            var point=DeformPoint(rig,i);
            points[i]=new(point.X,point.Y);
        }
        using var vertices=SKVertices.CreateCopy(SKVertexMode.Triangles,points,uv,null,visibleIndices);
        canvas.DrawVertices(vertices,SKBlendMode.Modulate,cachedPaint);
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
