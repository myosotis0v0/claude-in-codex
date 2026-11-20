using SkiaSharp;
using System.Numerics;

namespace MotionLab;
internal sealed partial class Renderer
{
    private Mesh[]? touchMeshes;
    private TextureWarpMesh? touchRibbonMesh;
    internal void DrawTouch(SKCanvas c,Pose p,HeadTouch g,float scale,float ox,float oy)
        =>DrawTouch(c,p,g.Snapshot(),scale,ox,oy,Hands.Snapshot());
    internal void DrawTouch(SKCanvas c,Pose p,HeadTouchPose g,float scale,float ox,float oy,DeskPose hands){
        if(!g.Connected){
            if(Math.Abs(g.Squash)<.00001f){Draw(c,p,g.Sway,g.Tip,scale,ox,oy,front:g.Front,desk:hands);return;}
            Rig.Update(p,g.Sway,g.Tip,g.Front);c.Clear(SKColors.Transparent);
            c.Save();c.Scale(scale);c.Translate(ox,oy);
            c.Save();c.Translate(627,1254);c.Scale(1+.34f*g.Squash,1-g.Squash);c.Translate(-627,-1254);
            DrawHead(c,p);c.Restore();DrawForeground(c,hands);c.Restore();return;
        }
        touchMeshes??=[new(Profile,"hair-front-left",touchTips:true),new(Profile,"hair-front-center",touchTips:true),
            new(Profile,"hair-front-right",touchTips:true)];
        Rig.Update(new Pose(),g.TipRoll*.21f,g.TipRoll*.21f,g.TouchTips);
        c.Clear(SKColors.Transparent);c.Save();c.Scale(scale);c.Translate(ox,oy);
        foreach(var part in Profile.TouchLayers){
            c.Save();
            bool head=part is not ("neck" or "cape-shoulders" or "chest-ornament");
            if(part is "rear-hair" or "hair-backing" or "hair-curtain")
                c.ClipRect(new SKRect(-2000,-2000,3254,1254),SKClipOperation.Intersect,false);
            if(head){c.Translate(p.HeadX,p.HeadY);c.RotateDegrees(p.HeadAngle,Profile.TouchPivot.X,Profile.TouchPivot.Y);}
            switch(part){
                case "rear-hair":DrawTouchRear(c,Meshes[4],part);break;
                case "neck":DrawPart(c,"neck",paint);break;
                case "hair-backing":DrawTouchRear(c,FrontMeshes[3],part);break;
                case "hair-curtain":DrawTouchRear(c,RearCurtain!,part);break;
                case "cape-shoulders" or "chest-ornament" or "face":DrawPart(c,part,paint);break;
                case "features":
                    DrawEye(c,true,p.EyeLeft,p);DrawEye(c,false,p.EyeRight,p);DrawBrows(c,p);DrawMouth(c,p);
                    using(var blush=new SKPaint{IsAntialias=true,Color=new SKColor(243,118,128,(byte)(75*p.Blush))}){
                        c.DrawOval(new SKRect(337,861,437,900),blush);c.DrawOval(new SKRect(810,861,910,900),blush);}
                    break;
                case "hair-front-left":DrawRear(c,touchMeshes[0],part);break;
                case "hair-front-right":DrawRear(c,touchMeshes[2],part);break;
                case "hair-front-center":DrawRear(c,touchMeshes[1],part);break;
                case "hair-cap":DrawPart(c,part,paint);break;
                case "accessory":DrawTouchAccessory(c,g.TipRoll);break;
            }
            c.Restore();
        }
        DrawForeground(c,hands);c.Restore();
    }
    internal void DrawTouchAccessory(SKCanvas canvas,float tipRoll)
    {
        var ribbon=new TouchRibbonWarp(Profile.Weights.Accessory,Rig.Bones[9].Rest,tipRoll);
        DrawWarpedPart(canvas,touchRibbonMesh??=new(18,28),images["accessory"],ribbon.Offset);
    }
    private void DrawTouchRear(SKCanvas c,Mesh mesh,string name){
        var p=images[name];mesh.Draw(c,Rig,p.Bitmap,p.X,p.Y,highQuality);
    }
}

internal readonly struct TouchRibbonWarp
{
    private readonly WeightBand tails;
    private readonly Matrix3x2 rotation;

    internal TouchRibbonWarp(WeightBand accessory,Vector2 attachment,float tipRoll)
    {
        // Leave the flower and tied bow rigid. The hanging length bends from the same delayed stroke as the hair tips.
        tails=new(accessory.Start+.30f*accessory.Length,.80f*accessory.Length);
        var angle=Math.Clamp(.40f*tipRoll,-4.2f,4.2f)*MathF.PI/180;
        rotation=Matrix3x2.CreateRotation(angle,attachment);
    }

    internal Vector2 Offset(Vector2 point)
    {
        var weight=tails.Smooth(point.Y);
        return weight==0?default:(Vector2.Transform(point,rotation)-point)*weight;
    }
}
