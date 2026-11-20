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
        "hair-front-center" or "hair-cap" or "hair";

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
    private const float CenterX=627;
    private static readonly Vector2 FlowerPin=new(1015,368);
    private static float Smooth(float u){u=Math.Clamp(u,0,1);return u*u*(3-2*u);}

    internal Vector2 HeadWarp(Vector2 point)
    {
        // One Angle-Y keyform for silhouette and features: crown recedes, hairline rises, chin lifts less.
        var crown=1-Smooth((point.Y-34)/245);
        var forward=Smooth((point.Y-93)/229);
        var chin=Smooth((point.Y-848)/215);
        var dy=34*crown-22*forward*(1-.46f*chin);
        var span=-.044f*(1-Smooth((point.Y-80)/330))+.012f*Smooth((point.Y-575)/320);
        return new(weight*(point.X-CenterX)*span,weight*dy);
    }
    internal Vector2 SideLock(Vector2 point)
    {
        // The roots rotate with the head; the hanging lengths retain part of their downward drape.
        var hanging=weight*Smooth((point.Y-540)/450);
        return new(-.018f*(point.X-CenterX)*hanging,10*hanging);
    }
    internal Vector2 Accessory(Vector2 point)
    {
        // Carry the pin with the temple, keeping the petals together and the ribbons close to vertical.
        var local=point-FlowerPin;
        var tails=Smooth((point.Y-610)/450);
        return HeadWarp(FlowerPin)+weight*new Vector2(-local.X*(.025f+.027f*tails),-.038f*local.Y*(1-tails)+10*tails);
    }
    internal Vector2 RearHair(Vector2 point)=>new(0,7*weight*Smooth((point.Y-610)/385));
    internal Vector2 Backing(Vector2 point)=>Vector2.Lerp(HeadWarp(point),RearHair(point),Smooth((point.Y-330)/340));
}
