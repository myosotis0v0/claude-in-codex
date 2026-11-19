using SkiaSharp;

namespace MotionLab;
internal sealed partial class Renderer
{
    private Mesh[]? touchMeshes;
    private static readonly string[] TouchParts=["rear","neck","backing","curtain","chest","skin","features","left","right","center","cap","bow"];
    internal void DrawTouch(SKCanvas c,Pose p,HeadTouch g,float scale,float ox,float oy){
        if(!g.Connected){
            if(Math.Abs(g.Squash)<.00001f){Draw(c,p,g.Sway,g.Tip,scale,ox,oy,front:g.Front);return;}
            Rig.Update(p,g.Sway,g.Tip,g.Front);c.Clear(SKColors.Transparent);
            c.Save();c.Scale(scale);c.Translate(ox,oy);
            c.Save();c.Translate(627,1254);c.Scale(1+.34f*g.Squash,1-g.Squash);c.Translate(-627,-1254);
            DrawHead(c,p);c.Restore();DrawForeground(c);c.Restore();return;
        }
        if(touchMeshes==null){
            touchMeshes=[new("hair-front-left",40,44),new("hair-front-center",32,40),new("hair-front-right",40,44)];
            for(int j=0;j<3;j++){
                var mesh=touchMeshes[j];int mid=13+j*2;
                for(int k=0;k<mesh.Rest.Length;k++){
                    float envelope=Math.Clamp((mesh.Rest[k].Y-650)/215,0,1);
                    envelope=envelope*envelope*(3-2*envelope);
                    mesh.Weights[k]=[new(1,1-envelope),new(mid+1,envelope)];
                }
            }
        }
        Rig.Update(new Pose(),g.TipRoll*.21f,g.TipRoll*.21f,g.TouchTips);
        c.Clear(SKColors.Transparent);c.Save();c.Scale(scale);c.Translate(ox,oy);
        foreach(var part in TouchParts){
            c.Save();
            bool head=part is not ("neck" or "chest");
            if(part is "rear" or "backing" or "curtain")
                c.ClipRect(new SKRect(-2000,-2000,3254,1254),SKClipOperation.Intersect,false);
            if(head){c.Translate(p.HeadX,p.HeadY);c.RotateDegrees(p.HeadAngle,627,1058);}
            switch(part){
                case "rear":DrawTouchRear(c,Meshes[4],"rear-hair");break;
                case "neck":DrawPart(c,"neck",paint);break;
                case "backing":DrawTouchRear(c,FrontMeshes[3],"hair-backing");break;
                case "curtain":DrawTouchRear(c,RearCurtain!,"hair-curtain");break;
                case "chest":DrawPart(c,"cape-shoulders",paint);DrawPart(c,"chest-ornament",paint);break;
                case "skin":DrawPart(c,"face",paint);break;
                case "features":
                    DrawEye(c,true,p.EyeLeft,p);DrawEye(c,false,p.EyeRight,p);DrawBrows(c,p);DrawMouth(c,p);
                    using(var blush=new SKPaint{IsAntialias=true,Color=new SKColor(243,118,128,(byte)(75*p.Blush))}){
                        c.DrawOval(new SKRect(337,861,437,900),blush);c.DrawOval(new SKRect(810,861,910,900),blush);}
                    break;
                case "left":DrawRear(c,touchMeshes![0],"hair-front-left");break;
                case "right":DrawRear(c,touchMeshes![2],"hair-front-right");break;
                case "center":DrawRear(c,touchMeshes![1],"hair-front-center");break;
                case "cap":DrawPart(c,"hair-cap",paint);break;
                case "bow":DrawPart(c,"accessory",paint);break;
            }
            c.Restore();
        }
        DrawForeground(c);c.Restore();
    }
    private void DrawTouchRear(SKCanvas c,Mesh mesh,string name){
        var p=images[name];mesh.Draw(c,Rig,p.Bitmap,p.X,p.Y,highQuality);
    }
}
