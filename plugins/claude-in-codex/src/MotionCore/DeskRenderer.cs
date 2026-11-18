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
        if(!images.ContainsKey("cape-shoulders")){DrawMesh(c,3,"clothes-paws");return;}
        if(!Hands.Enabled||deskRoot==null){
            DrawPart(c,"mouse-paw",paint);DrawPart(c,"keyboard-paw",paint);return;
        }
        deskMouse??=SKBitmap.Decode(Path.Combine(deskRoot,"mouse.png"));
        deskKeyboard??=SKBitmap.Decode(Path.Combine(deskRoot,"keyboard.png"));
        var h=Hands;
        c.DrawBitmap(deskMouse,new SKRect(213+h.MouseX,1200,418+h.MouseX,1278),sampling,paint);
        c.DrawBitmap(deskKeyboard,new SKRect(419,1201,1154,1278),sampling,paint);
        void Hand(string name,float x,float y){
            c.Save();c.Translate(x,y);DrawPart(c,name,paint);c.Restore();
        }
        Hand(h.Pointer.Contact?"mouse-paw-contact":"mouse-paw",h.Pointer.X,h.Pointer.Y);
        Hand(h.Keyboard.Contact?"keyboard-paw-contact":"keyboard-paw",h.Keyboard.X,h.Keyboard.Y);
    }
}
