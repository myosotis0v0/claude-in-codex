using SkiaSharp;
using System.IO;

namespace MotionLab;
internal sealed partial class Renderer
{
    internal readonly DeskHands Hands=new();
    private SKBitmap? deskMouse,deskKeyboard;
    private string? deskRoot;
    private static SKPath HandRegion(bool right){
        using var path=new SKPathBuilder();
        // Follow the inner sleeve seam where the mitten touches the blouse.
        path.MoveTo(0,1020);path.LineTo(451,1020);path.LineTo(451,1145);
        path.LineTo(443,1160);path.LineTo(436,1173);path.LineTo(424,1189);
        path.LineTo(412,1201);path.LineTo(415,1254);path.LineTo(0,1254);path.Close();
        var shape=path.Detach();
        if(right)shape.Transform(new SKMatrix{ScaleX=-1,TransX=1244,ScaleY=1,Persp2=1});
        return shape;
    }
    private readonly SKPath mouseHand=HandRegion(false),keyHand=HandRegion(true);
    private void DrawForeground(SKCanvas c){
        if(!Hands.Enabled||deskRoot==null){DrawMesh(c,3,"clothes-paws");return;}
        deskMouse??=SKBitmap.Decode(Path.Combine(deskRoot,"mouse.png"));
        deskKeyboard??=SKBitmap.Decode(Path.Combine(deskRoot,"keyboard.png"));
        var h=Hands;
        c.DrawBitmap(deskMouse,new SKRect(213+h.MouseX,1200+h.MouseY,418+h.MouseX,1278+h.MouseY),sampling,paint);
        c.DrawBitmap(deskKeyboard,new SKRect(419,1201,1154,1278),sampling,paint);
        // Short sleeve bridge under a reaching wrist; the chest flower stays fixed.
        if(Math.Abs(h.KeyX)>5){
            using var sleeve=new SKPaint{IsAntialias=true,StrokeCap=SKStrokeCap.Round,StrokeWidth=47,Color=Ink};
            float x=920+h.KeyX,y=1178+h.KeyY;
            c.DrawLine(798,1181,x,y,sleeve);sleeve.StrokeWidth=32;sleeve.Color=new SKColor(251,231,213);
            c.DrawLine(798,1181,x,y,sleeve);
        }
        c.Save();c.ClipPath(mouseHand,SKClipOperation.Difference);c.ClipPath(keyHand,SKClipOperation.Difference);
        DrawPart(c,"clothes-paws",paint);c.Restore();
        void Hand(SKPath shape,float x,float y){
            c.Save();c.Translate(x,y);c.ClipPath(shape);DrawPart(c,"clothes-paws",paint);c.Restore();
        }
        Hand(mouseHand,h.MouseX,h.MouseY);Hand(keyHand,h.KeyX,h.KeyY);
    }
}
