using SkiaSharp;

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
    private static float Smooth(float u){u=Math.Clamp(u,0,1);return u*u*(3-2*u);}
    internal static SKPoint MenuTipOffset(SKPoint point,float pitch)
    {
        // Fall off toward the crown, cheeks and lower rear hair; only the hanging ends follow through.
        var left=Smooth((359-point.X)/188)*Smooth((point.Y-690)/210)*Smooth((1055-point.Y)/123);
        var right=Smooth((point.X-987)/156)*Smooth((point.Y-620)/215)*Smooth((1152-point.Y)/142);
        return new(pitch*(12*right-11*left),-pitch*(28*left+27*right));
    }
    private void DrawMenuFrame(SKCanvas target,Pose p,float scale,float ox,float oy,bool debug,DeskPose hands){
        var bounds=target.DeviceClipBounds;int width=bounds.Right,height=bounds.Bottom;
        if(width<=0||height<=0)return;
        if(menuBuffer?.Width!=width||menuBuffer.Height!=height){
            menuCanvas?.Dispose();menuBuffer?.Dispose();menuBuffer=new SKBitmap(width,height,SKColorType.Bgra8888,SKAlphaType.Premul);
            menuCanvas=new SKCanvas(menuBuffer);
            for(int j=0;j<=Grid;j++)for(int i=0;i<=Grid;i++)
                menuUv[j*(Grid+1)+i]=new(i*width/(float)Grid,j*height/(float)Grid);
        }
        var c=menuCanvas!;c.Clear(SKColors.Transparent);c.Save();c.Scale(scale);c.Translate(ox,oy);
        DrawHead(c,p);c.Restore();c.Flush();
        var head=Rig.ToSkia(Rig.Skin[1]);head.TryInvert(out var inv);
        for(int k=0;k<menuUv.Length;k++){
            var uv=menuUv[k];var v=inv.MapPoint(uv.X/scale-ox,uv.Y/scale-oy);
            var x=v.X-627;var w=p.MenuPitchWeight*Smooth((1040-v.Y)/120);
            var cap=MathF.Exp(-MathF.Pow((v.Y-365)/270,2))*Smooth((v.Y-115)/180);
            var jaw=MathF.Exp(-MathF.Pow((v.Y-949)/122,2));
            var side=Math.Min(1,Math.Abs(x)/430);var centre=Math.Max(0,1-x*x/(615*615));
            var ends=MenuTipOffset(v,p.MenuPitchWeight);
            var dest=head.MapPoint(v.X+w*x*(.041f*jaw-.008f*cap)+ends.X,v.Y-w*(24*cap*centre+19*jaw*side*side)+ends.Y);
            menuPoints[k]=new((dest.X+ox)*scale,(dest.Y+oy)*scale);
        }
        using var shader=menuBuffer.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,sampling);
        using var ink=new SKPaint{Shader=shader};
        using var mesh=SKVertices.CreateCopy(SKVertexMode.Triangles,menuPoints,menuUv,null,MenuIndices);
        target.DrawVertices(mesh,SKBlendMode.Modulate,ink);target.Save();target.Scale(scale);target.Translate(ox,oy);
        DrawForeground(target,hands);if(debug)DrawDebug(target);target.Restore();
    }
}
