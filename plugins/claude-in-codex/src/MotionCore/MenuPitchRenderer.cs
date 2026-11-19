using SkiaSharp;
using System.Numerics;

namespace MotionLab;

internal sealed partial class Renderer
{
    private SKCanvas? menuCanvas;
    private SKBitmap? menuBuffer;
    private const int Grid=40;
    private readonly SKPoint[] menuUv=new SKPoint[(Grid+1)*(Grid+1)],menuPoints=new SKPoint[(Grid+1)*(Grid+1)];
    private static readonly ushort[] MenuIndices=BuildIndices();

    private static ushort[] BuildIndices(){
        var list=new List<ushort>(Grid*Grid*6);
        for(int j=0;j<Grid;j++)for(int i=0;i<Grid;i++){
            var k=j*(Grid+1)+i;
            foreach(var n in new[]{k,k+1,k+Grid+2,k,k+Grid+2,k+Grid+1})list.Add((ushort)n);
        }
        return list.ToArray();
    }
    private static void DrawMenuNeck(SKCanvas c,float lift){
        using var ink=new SKPaint{IsAntialias=true,Color=new SKColor(251,232,222,(byte)(255*lift))};
        using var path=new SKPathBuilder();
        path.MoveTo(573,1028);path.QuadTo(582,1058,580,1072);path.QuadTo(627,1090,674,1072);
        path.QuadTo(672,1055,682,1028);path.Close();using var shape=path.Detach();c.DrawPath(shape,ink);
    }
    internal static bool MenuUsesHeadWarp(string layer)=>layer is "face" or "hair-front-left" or "hair-front-right" or
        "hair-front-center" or "hair-cap" or "hair" or "accessory";

    private void DrawMenuFrame(SKCanvas target,Pose p,float scale,float ox,float oy,bool debug,DeskPose hands){
        var bounds=target.DeviceClipBounds;int width=bounds.Right,height=bounds.Bottom;
        if(width<=0||height<=0)return;
        if(menuBuffer?.Width!=width||menuBuffer.Height!=height){
            menuCanvas?.Dispose();menuBuffer?.Dispose();menuBuffer=new SKBitmap(width,height,SKColorType.Bgra8888,SKAlphaType.Premul);
            menuCanvas=new SKCanvas(menuBuffer);
            for(int j=0;j<=Grid;j++)for(int i=0;i<=Grid;i++)
                menuUv[j*(Grid+1)+i]=new(i*width/(float)Grid,j*height/(float)Grid);
        }
        var projection=new MenuPitchProjection(p.MenuPitchWeight);
        var c=menuCanvas!;bool collecting=false;
        void FlushHead()
        {
            c.Restore();c.Flush();
            WarpMenuSurface(target,scale,ox,oy,projection);
            collecting=false;
        }
        // Keep the authored order even if a character interleaves a rigid layer with the warped head.
        foreach(var layer in Profile.NormalLayers)
        {
            if(MenuUsesHeadWarp(layer))
            {
                if(!collecting){c.Clear(SKColors.Transparent);c.Save();c.Scale(scale);c.Translate(ox,oy);collecting=true;}
                DrawHeadLayer(c,p,layer,projection);
            }
            else
            {
                if(collecting)FlushHead();
                target.Save();target.Scale(scale);target.Translate(ox,oy);
                DrawHeadLayer(target,p,layer,projection);target.Restore();
            }
        }
        if(collecting)FlushHead();
        target.Save();target.Scale(scale);target.Translate(ox,oy);
        DrawForeground(target,hands);if(debug)DrawDebug(target);target.Restore();
    }
    private void WarpMenuSurface(SKCanvas target,float scale,float ox,float oy,MenuPitchProjection projection)
    {
        var head=Rig.ToSkia(Rig.Skin[1]);head.TryInvert(out var inv);
        for(int k=0;k<menuUv.Length;k++){
            var uv=menuUv[k];var v=inv.MapPoint(uv.X/scale-ox,uv.Y/scale-oy);
            var shift=projection.HeadWarp(new(v.X,v.Y));
            var dest=head.MapPoint(v.X+shift.X,v.Y+shift.Y);
            menuPoints[k]=new((dest.X+ox)*scale,(dest.Y+oy)*scale);
        }
        using var shader=menuBuffer!.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,sampling);
        using var ink=new SKPaint{Shader=shader};
        using var mesh=SKVertices.CreateCopy(SKVertexMode.Triangles,menuPoints,menuUv,null,MenuIndices);
        target.DrawVertices(mesh,SKBlendMode.Modulate,ink);
    }
}

internal readonly struct MenuPitchProjection(float weight)
{
    private const float Tilt=14*MathF.PI/180,EyeDepth=120,FrontDepth=235,RibbonDepth=285;
    private const float CenterX=627,PivotY=550,FocalLength=2400;
    private readonly float sin=MathF.Sin(weight*Tilt),cos=MathF.Cos(weight*Tilt);
    private static float Smooth(float u){u=Math.Clamp(u,0,1);return u*u*(3-2*u);}

    internal Vector2 FrontTip(Vector2 point)=>TipOffset(point,FrontDepth,Smooth((point.Y-670)/230));
    internal Vector2 RibbonTip(Vector2 point)=>TipOffset(point,RibbonDepth,Smooth((point.Y-640)/225));
    internal Vector2 RearHair(Vector2 point)=>new(0,7*weight*Smooth((point.Y-610)/385));
    internal Vector2 EyePlane(Vector2 point)=>Project(point,EyeDepth);

    private Vector2 Project(Vector2 point,float depth)
    {
        // Fit the pitch to the existing 31px peak eye lift, then project the ends on deeper planes.
        var y=point.Y-PivotY;
        var zAfter=y*sin+depth*cos;
        var scale=(FocalLength-depth)/(FocalLength-zAfter);
        return new((point.X-CenterX)*(scale-1),(y*cos-depth*sin)*scale-y);
    }
    private Vector2 TipOffset(Vector2 point,float depth,float blend)
    {
        if(blend==0||weight==0)return default;
        // The tip also passes through the shared head warp. Remove that contribution before skinning.
        var target=point+Project(point,depth);
        var source=target-HeadWarp(target);
        return (target-HeadWarp(source)-point)*blend;
    }
    internal Vector2 HeadWarp(Vector2 point)
    {
        var x=point.X-CenterX;var w=weight*Smooth((1040-point.Y)/120);
        var cap=MathF.Exp(-MathF.Pow((point.Y-365)/270,2))*Smooth((point.Y-115)/180);
        var jaw=MathF.Exp(-MathF.Pow((point.Y-949)/122,2));
        var side=Math.Min(1,Math.Abs(x)/430);var centre=Math.Max(0,1-x*x/(615*615));
        return new(w*x*(.041f*jaw-.008f*cap),-w*(24*cap*centre+19*jaw*side*side));
    }
}
