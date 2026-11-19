using SkiaSharp;
using System.IO;

namespace MotionLab;
internal sealed partial class Renderer
{
    internal readonly DeskHands Hands=new();
    private const int LittleEnd=298,MiddleEnd=348,PawCanvasWidth=1254;
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
    private void DrawForeground(SKCanvas c,DeskPose h,Pose? action=null){
        if(!images.ContainsKey("cape-shoulders")){DrawMesh(c,3,"clothes-paws");return;}
        var linked=h.Enabled&&deskRoot!=null;
        c.Save();if(linked)c.Translate(DeskHands.DeskShiftX,0);
        foreach(var layer in Profile.DeskLayers)
        {
            switch(layer){
                case "desk-mouse" when linked:
                    deskMouse??=SKBitmap.Decode(Path.Combine(deskRoot!,"mouse.png"));
                    c.DrawBitmap(deskMouse,new SKRect(213+h.MouseX,1200,418+h.MouseX,1278),sampling,paint);break;
                case "desk-keyboard" when linked:
                    deskKeyboard??=SKBitmap.Decode(Path.Combine(deskRoot!,"keyboard.png"));
                    c.DrawBitmap(deskKeyboard,new SKRect(419,1201,1154,1278),sampling,paint);break;
                case "mouse-paw":
                    if(action?.CelId=="complete")DrawPlacardHand(c,action.CelTime,linked?h.MouseX:0,linked?h.PointerX:0,linked?h.PointerY:0);
                    else if(!linked)DrawPart(c,layer,paint);
                    else DrawDeskMouseHand(c,h);
                    break;
                case "keyboard-paw":
                    if(!linked)DrawPart(c,layer,paint);
                    else DrawHand(c,h.KeyboardContact?"keyboard-paw-contact":layer,h.KeyboardX,h.KeyboardY);
                    break;
            }
        }
        c.Restore();
    }
    private void DrawHand(SKCanvas c,string name,float x,float y){
        c.Save();c.Translate(x,y);DrawPart(c,name,paint);c.Restore();
    }
    private void DrawDeskMouseHand(SKCanvas c,DeskPose h){
        var pose=h.MousePose;
        if(h.PointerAway||pose==FingerPose.Rest)DrawHand(c,h.PointerContact?"mouse-paw-contact":"mouse-paw",h.PointerX,h.PointerY);
        else{
            // Disjoint source-space strips compose chords without overdrawing translucent edges.
            c.Save();c.Translate(h.PointerX,h.PointerY);
            void Strip(float from,float to,FingerPose finger,string variant){
                c.Save();c.ClipRect(new SKRect(from,0,to,1254),SKClipOperation.Intersect,false);
                DrawPart(c,(pose&finger)!=0?variant:"mouse-paw",paint);c.Restore();
            }
            Strip(0,LittleEnd,FingerPose.Little,"mouse-paw-right");
            Strip(LittleEnd,MiddleEnd,FingerPose.Middle,"mouse-paw-wheel");
            Strip(MiddleEnd,PawCanvasWidth,FingerPose.Index,"mouse-paw-left");
            c.Restore();
        }
    }
}
