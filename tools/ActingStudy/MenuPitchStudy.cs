using ClaudeCompanion;
using MotionLab;
using SkiaSharp;
using System.IO;
using System.Windows;

internal static class MenuPitchStudy
{
    internal static void Run(string root,Renderer r,int size)
    {
        var ctl=new MotionController();
        var dir=Path.Combine(root,"artifacts/menu-pitch-volume");Directory.CreateDirectory(dir);
        for(int i=0;i<72;i++){
            if(i==8)ctl.LookAtMenu(new Vector(-.25,-.95));
            var p=ctl.Step(1d/30,new Vector(.12,0),true);
            var t=(i-8)/30d;
            var angle=12*Acting.Beat(t,.08,.36,.66,1.18)-.7f*Acting.Beat(t,.86,1.08,1.12,1.38);
            using var a=r.MenuPitch(p,ctl.Hair,ctl.Tip,size,ctl.FrontHair,angle,false);
            using var b=r.MenuPitch(p,ctl.Hair,ctl.Tip,size,ctl.FrontHair,angle,true);
            if(angle==0&&!a.Bytes.SequenceEqual(b.Bytes))throw new InvalidDataException($"Neutral mismatch at {i}");
            using var pair=new SKBitmap(2*size,size);using var c=new SKCanvas(pair);
            c.Clear(SKColors.Transparent);var s=new SKSamplingOptions(SKFilterMode.Nearest);
            c.DrawBitmap(a,0,0,s);c.DrawBitmap(b,size,0,s);
            using var png=pair.Encode(SKEncodedImageFormat.Png,100);using var f=File.Create(Path.Combine(dir,$"{i:000}.png"));png.SaveTo(f);
        }
    }
}

namespace MotionLab {
internal sealed partial class Renderer
{
    // Planes in head-local coordinates tilt around a common pivot. At rest every mapping is identity.
    static SKMatrix Pitch(float degrees,float depth){
        const float cx=627,cy=1040,lens=8600;
        float a=degrees*MathF.PI/180,s=MathF.Sin(a),co=MathF.Cos(a);
        var m=new SKMatrix{ScaleX=1,ScaleY=co,TransY=-depth*s,Persp1=-s/lens,Persp2=1-depth*(co-1)/lens};
        return SKMatrix.Concat(SKMatrix.CreateTranslation(cx,cy),SKMatrix.Concat(m,SKMatrix.CreateTranslation(-cx,-cy)));
    }
    internal SKBitmap MenuPitch(Pose p,float sway,float tip,int size,FrontHairPose front,float angle,bool volume,bool keyform=false)
    {
        if(angle==0)return Render(p,sway,tip,size,front:front);
        // Remove most of the previous translation as pitch takes over.
        var lift=Math.Clamp(angle/12,0,1);
        Rig.Update(keyform?p:p with{HeadY=p.HeadY+13*lift},sway,tip,front);
        var b=new SKBitmap(size,size,SKColorType.Bgra8888,SKAlphaType.Premul);using var c=new SKCanvas(b);
        c.Clear(SKColors.Transparent);c.Scale(size/1400f);c.Translate(73,73);
        DrawMesh(c,4,"rear-hair");DrawMesh(c,2,"neck");DrawFront(c,3,"hair-backing");DrawRear(c,RearCurtain!,"hair-curtain");
        var head=Rig.ToSkia(Rig.Skin[1]);head.TryInvert(out var inverse);
        // The original neck is underneath opaque rear hair. Extend only the narrow
        // collar opening in front of that hair and behind the jaw and clothes.
        using(var neck=new SKPaint{IsAntialias=true,Color=new SKColor(251,232,222,(byte)(255*lift))})
        using(var path=new SKPathBuilder()){
            path.MoveTo(573,1028);path.QuadTo(582,1058,580,1072);
            path.QuadTo(627,1090,674,1072);path.QuadTo(672,1055,682,1028);path.Close();
            using var shape=path.Detach();c.DrawPath(shape,neck);
        }
        void Plane(float depth,Action draw,bool skinned=false,float gain=1){
            c.Save();c.Concat(head);
            if(keyform)c.Translate(0,-depth*lift*.19f);else c.Concat(Pitch(angle*gain,depth));
            if(skinned)c.Concat(inverse);
            draw();c.Restore();
        }
        Plane(36,()=>DrawPart(c,"face",paint));
        Plane(164,()=>{DrawEye(c,true,p.EyeLeft,p);DrawEye(c,false,p.EyeRight,p);DrawBrows(c,p);});
        Plane(187,()=>DrawMouth(c,p));
        // Keep the cap, fringe and backing registered at their shared outline.
        DrawFront(c,0,"hair-front-left");DrawFront(c,2,"hair-front-right");DrawFront(c,1,"hair-front-center");
        c.Save();c.Concat(head);DrawPart(c,"hair-cap",paint);c.Restore();
        DrawMesh(c,1,"accessory");
        if(!volume){DrawMesh(c,3,"clothes-paws");return b;}
        c.Flush();
        var result=PitchShell(b,head,inverse,angle,keyform);
        using var resolved=new SKCanvas(result);
        resolved.Scale(size/1400f);resolved.Translate(73,73);DrawMesh(resolved,3,"clothes-paws");
        b.Dispose();return result;
    }

    // A shared head-local cage keeps painted edges coincident across face, scalp and backing.
    // It feathers to identity at the underside of the chin, before the collar and resting paws.
    static SKBitmap PitchShell(SKBitmap src,SKMatrix head,SKMatrix inverse,float angle,bool keyform)
    {
        const int n=40;var size=src.Width;var scale=size/1400f;
        var uv=new SKPoint[(n+1)*(n+1)];var xy=new SKPoint[uv.Length];var tris=new ushort[n*n*6];
        var radians=angle*.85f*MathF.PI/180;var sn=MathF.Sin(radians);var cs=MathF.Cos(radians);
        static float Smooth(float u){u=Math.Clamp(u,0,1);return u*u*(3-2*u);}
        for(int j=0;j<=n;j++)for(int i=0;i<=n;i++){
            var k=j*(n+1)+i;var pixel=uv[k]=new SKPoint(i*size/(float)n,j*size/(float)n);
            var v=inverse.MapPoint(pixel.X/scale-73,pixel.Y/scale-73);
            float x=v.X-627,y=v.Y-1040;
            var cheek=MathF.Exp(-MathF.Pow((v.Y-866)/170,2));
            var side=Math.Min(1,Math.Abs(x)/430);
            var w=Smooth((1040-v.Y)/120);
            var depth=52*Smooth((v.Y-60)/540)*(1-.72f*side*side);
            var perspective=6500/(6500-y*sn-depth*(cs-1));
            var projectedX=x*perspective*(1+.025f*cheek*angle/12);
            var projectedY=(y*cs-depth*sn)*perspective-14*cheek*side*side*angle/12;
            if(keyform){
                // Rotate markings over the crown without lowering its silhouette. The
                // lower cheeks turn forward; neck contact and the jaw centre stay put.
                var kf=angle/12;
                var cap=MathF.Exp(-MathF.Pow((v.Y-365)/270,2))*Smooth((v.Y-115)/180);
                var centre=MathF.Max(0,1-x*x/(615*615));
                var jaw=MathF.Exp(-MathF.Pow((v.Y-949)/122,2));
                projectedX=x*(1+kf*(.041f*jaw-.008f*cap));
                projectedY=y-kf*(24*cap*centre+19*jaw*side*side);
            }
            var dest=head.MapPoint(v.X+w*(projectedX-x),v.Y+w*(projectedY-y));
            xy[k]=new((dest.X+73)*scale,(dest.Y+73)*scale);
            if(j<n&&i<n){
                int t=(j*n+i)*6;
                foreach(var index in new[]{k,k+1,k+n+2,k,k+n+2,k+n+1})tris[t++]=(ushort)index;
            }
        }
        var result=new SKBitmap(size,size,SKColorType.Bgra8888,SKAlphaType.Premul);
        using var canvas=new SKCanvas(result);canvas.Clear(SKColors.Transparent);
        using var shader=src.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var paint=new SKPaint{Shader=shader};
        using var mesh=SKVertices.CreateCopy(SKVertexMode.Triangles,xy,uv,null,tris);
        canvas.DrawVertices(mesh,SKBlendMode.Modulate,paint);return result;
    }
}}
