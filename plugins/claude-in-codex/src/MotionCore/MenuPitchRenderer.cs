using SkiaSharp;
using System.Numerics;

namespace MotionLab;

internal sealed partial class Renderer
{
    private SKBitmap? pitchFaceBuffer;
    private SKCanvas? pitchFaceCanvas;
    private TextureWarpMesh? pitchFaceMesh,pitchCapMesh,pitchCrownMesh;
    private readonly string? menuHairRoot;
    private MenuHair? menuHair;
    internal bool HasMenuHair=>menuHairRoot!=null;

    internal void PrepareMenuHair()
    {
        if(menuHair!=null||menuHairRoot is not {} root)return;
        menuHair=new MenuHair(Profile,root,FrontMeshes[3],highQuality);
    }

    private void DrawMenuFrame(SKCanvas canvas,Pose pose,float scale,float x,float y,bool debug,DeskPose hands)
    {
        var projection=new MenuPitchProjection(pose.MenuPitchWeight,Rig.Bones[1].Rest,Rig.Bones[11].Rest);
        var form=new MenuCrownForm(pose.MenuPitchWeight);
        PrepareMenuHair();
        var hair=menuHair;
        var head=Rig.Skin[1];
        Vector2 World(Vector2 offset)=>Vector2.TransformNormal(offset,head);
        Vector2 Face(Vector2 p)=>form.Face(p,projection.Face(p));
        void Front(int index,string name,Func<Vector2,Vector2> offset)
        {
            if(hair==null)DrawFront(canvas,index,name,p=>World(offset(p)));
            else DrawTextureMesh(canvas,hair.Meshes[index],hair.Parts[index],p=>World(form.Front(p,offset(p))));
        }
        canvas.Save();canvas.Scale(scale);canvas.Translate(x,y);
        foreach(var layer in Profile.NormalLayers)
        {
            switch(layer)
            {
                case "rear-hair":DrawMesh(canvas,4,layer,p=>World(projection.Rear(p)));break;
                case "hair-backing" when hair!=null:
                    DrawTextureMesh(canvas,hair.Meshes[3],hair.Parts[3],p=>World(projection.Rear(p)));
                    DrawWarpedPart(canvas,pitchCrownMesh??=new(36,22),hair.Crown,form.Crown);break;
                case "hair-backing":DrawFront(canvas,3,layer,p=>World(projection.Backing(p)));break;
                case "hair-curtain" when RearCurtain!=null:DrawRear(canvas,RearCurtain,layer,p=>World(projection.Rear(p)));break;
                case "menu-neck":DrawMenuNeck(canvas,pose.MenuPitchWeight);break;
                case "face":DrawPitchFace(canvas,pose,scale,hair==null?projection.Face:Face);break;
                case "hair-front-left":Front(0,layer,p=>projection.Side(p,false));break;
                case "hair-front-center":Front(1,layer,projection.Fringe);break;
                case "hair-front-right":Front(2,layer,p=>projection.Side(p,true));break;
                case "hair-cap":DrawWarpedPart(canvas,pitchCapMesh??=new(32,12),hair?.Cap??images[layer],
                    hair==null?projection.Cap:p=>form.Front(p,projection.Cap(p)));break;
                case "accessory":DrawMesh(canvas,1,layer,p=>World(projection.Ornament(p)));break;
                default:DrawHeadLayer(canvas,pose,layer);break;
            }
        }
        DrawForeground(canvas,hands);
        if(debug)DrawDebug(canvas);
        canvas.Restore();
    }

    private void DrawPitchFace(SKCanvas canvas,Pose pose,float scale,Func<Vector2,Vector2> offset)
    {
        var face=images["face"];
        var x=face.X-6;var y=face.Y-6;
        var raster=scale*1.5f;
        var w=(int)MathF.Ceiling((face.Bitmap.Width+12)*raster);
        var h=(int)MathF.Ceiling((face.Bitmap.Height+12)*raster);
        if(pitchFaceBuffer?.Width!=w||pitchFaceBuffer.Height!=h)
        {
            pitchFaceCanvas?.Dispose();pitchFaceBuffer?.Dispose();
            pitchFaceBuffer=new SKBitmap(w,h,SKColorType.Bgra8888,SKAlphaType.Premul);
            pitchFaceCanvas=new SKCanvas(pitchFaceBuffer);
        }
        var local=pitchFaceCanvas!;
        local.Clear(SKColors.Transparent);local.Save();local.Scale(raster);local.Translate(-x,-y);
        DrawFaceArtwork(local,pose);local.Restore();local.Flush();
        // Facial features share the skin surface; the hair is projected separately before covering it.
        (pitchFaceMesh??=new(24,30)).Draw(canvas,pitchFaceBuffer,x,y,w/raster,h/raster,Rig.Skin[1],offset,sampling);
    }

    private void DrawWarpedPart(SKCanvas canvas,TextureWarpMesh mesh,TexturePart part,Func<Vector2,Vector2> offset)=>
        mesh.Draw(canvas,part.Bitmap,part.X,part.Y,part.Bitmap.Width,part.Bitmap.Height,Rig.Skin[1],offset,sampling);

    private static void DrawMenuNeck(SKCanvas c,float lift)
    {
        using var ink=new SKPaint{IsAntialias=true,Color=new SKColor(251,232,222,(byte)(255*lift))};
        using var path=new SKPathBuilder();
        path.MoveTo(573,1028);path.QuadTo(582,1058,580,1072);path.QuadTo(627,1090,674,1072);
        path.QuadTo(672,1055,682,1028);path.Close();using var shape=path.Detach();c.DrawPath(shape,ink);
    }

    private sealed class TextureWarpMesh
    {
        private readonly SKPoint[] grid,uv,points;
        private readonly ushort[] indices;

        internal TextureWarpMesh(int columns,int rows)
        {
            grid=new SKPoint[(columns+1)*(rows+1)];uv=new SKPoint[grid.Length];points=new SKPoint[grid.Length];
            indices=new ushort[6*columns*rows];
            for(int j=0;j<=rows;j++)for(int i=0;i<=columns;i++)
            {
                var k=j*(columns+1)+i;grid[k]=new(i/(float)columns,j/(float)rows);
                if(i==columns||j==rows)continue;
                var n=(j*columns+i)*6;
                indices[n]=(ushort)k;indices[n+1]=(ushort)(k+1);indices[n+2]=(ushort)(k+columns+2);
                indices[n+3]=(ushort)k;indices[n+4]=(ushort)(k+columns+2);indices[n+5]=(ushort)(k+columns+1);
            }
        }

        internal void Draw(SKCanvas canvas,SKBitmap source,float x,float y,float width,float height,Matrix3x2 head,
            Func<Vector2,Vector2> offset,SKSamplingOptions sampling)
        {
            for(int k=0;k<grid.Length;k++)
            {
                var (u,v)=(grid[k].X,grid[k].Y);
                uv[k]=new(u*source.Width,v*source.Height);
                var p=new Vector2(x+u*width,y+v*height);
                var q=Vector2.Transform(p+offset(p),head);points[k]=new(q.X,q.Y);
            }
            using var shader=source.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,sampling);
            using var ink=new SKPaint{Shader=shader,IsAntialias=true};
            using var vertices=SKVertices.CreateCopy(SKVertexMode.Triangles,points,uv,null,indices);
            canvas.DrawVertices(vertices,SKBlendMode.Modulate,ink);
        }
    }
}

internal readonly record struct MenuCrownForm(float Weight)
{
    private static float Band(float v,float a,float b){var x=Math.Clamp((v-a)/(b-a),0,1);return x*x*(3-2*x);}

    internal Vector2 Face(Vector2 p,Vector2 original)
    {
        var dx=p.X-627;var side=Band(MathF.Abs(dx),90,280);
        var temple=Band(p.Y,540,715)*(1-Band(p.Y,778,885));
        var jaw=Band(p.Y,803,920)*(1-Band(p.Y,978,1052));
        var chinTurn=(1-Band(MathF.Abs(dx),52,176))*Band(p.Y,875,935)*(1-Band(p.Y,976,1045));
        return original+Weight*new Vector2(dx*(.030f*jaw-.014f*temple),-13*side*jaw-5*chinTurn);
    }

    internal Vector2 Front(Vector2 p,Vector2 depth)
    {
        var f=Band(p.Y,310,570);
        var scale=Weight*new Vector2(.012f*(p.X-627),-.016f*(p.Y-355)+3);
        var temple=Band(MathF.Abs(p.X-627),280,480)*Band(p.Y,295,440)*(1-Band(p.Y,472,610));
        return Vector2.Lerp(scale+Weight*temple*new Vector2(0,15),depth,f);
    }

    internal Vector2 Crown(Vector2 p)
    {
        // The visible crown rolls back from its temple attachment rather than sliding down as one piece.
        var aboveTemple=1-Band(p.Y,50,566);
        var across=1-.26f*Band(MathF.Abs(p.X-627),135,490);
        return Weight*new Vector2(-.018f*(p.X-627)*aboveTemple,7+48*aboveTemple*across);
    }
}

internal readonly struct MenuPitchProjection
{
    private readonly float weight,sine,cosine;
    private readonly Vector2 pivot,pin;
    private readonly Vector2 ornamentPin;
    private readonly float ornamentDepth;

    internal MenuPitchProjection(float weight,Vector2 pivot,Vector2 pin)
    {
        this.weight=weight;this.pivot=pivot;this.pin=pin;
        (sine,cosine)=MathF.SinCos(weight*.110f);
        ornamentDepth=Scalp(pin)+149;
        ornamentPin=Project(pin,ornamentDepth)+new Vector2(8*weight,0);
    }

    private static float Band(float v,float a,float b){var u=Math.Clamp((v-a)/(b-a),0,1);return u*u*(3-2*u);}
    private static float Lerp(float a,float b,float w)=>a+(b-a)*w;

    // Pitched around the same neck pivot, with depth measured in source artwork pixels.
    private Vector2 Project(Vector2 p,float z)
    {
        var v=p-pivot;
        var scale=6250/(6250-(v.Y*sine+z*(cosine-1)));
        return new Vector2(v.X*scale,(v.Y*cosine-z*sine)*scale)-v;
    }
    private float Scalp(Vector2 p)
    {
        var side=Band(MathF.Abs(p.X-pivot.X),170,550);
        return -144+264*Band(p.Y,42,540)-58*side*Band(p.Y,170,540);
    }
    private float CrownRecession(Vector2 p)
    {
        // The cap, backing and hair roots share the same curved surface above the hairline.
        var crown=1-Band(p.Y,36,274);
        var shoulder=1-.36f*Band(MathF.Abs(p.X-pivot.X),65,410);
        return -205*crown*shoulder;
    }
    internal Vector2 Cap(Vector2 p)=>Project(p,Scalp(p)+CrownRecession(p));
    internal Vector2 Face(Vector2 p)
    {
        var cheek=1-Band(MathF.Abs(p.X-pivot.X),85,335);
        var lower=Band(p.Y,865,1060);
        var rise=Band(p.Y,270,758);
        return Project(p,Lerp(45+rise*(103+83*cheek),104-19*(1-cheek),lower));
    }
    internal Vector2 Fringe(Vector2 p)
    {
        var belly=(1-Band(MathF.Abs(p.X-626),35,158))*Band(p.Y,285,590);
        var z=Scalp(p)+74*Band(p.Y,245,650)+115*belly*(1-.26f*Band(p.Y,685,850));
        return Project(p,z+CrownRecession(p));
    }
    internal Vector2 Side(Vector2 p,bool right)
    {
        var towardCheek=right?1-Band(p.X,765,1115):Band(p.X,130,458);
        var drape=Band(p.Y,745,1010);
        var z=Scalp(p)+(34+101*towardCheek)*Band(p.Y,280,785)-34*drape*(1-towardCheek);
        var tip=Band(p.Y,600,950)*(.35f+.65f*towardCheek);
        return Project(p,z+CrownRecession(p))+weight*tip*new Vector2(right?13:-13,3);
    }
    internal Vector2 Ornament(Vector2 p)
    {
        var v=p-pin;
        var flat=Project(p,ornamentDepth-.27f*v.X+.24f*v.Y)+new Vector2(8*weight,0);
        // Keep the petals on one tilted plane, and let the ribbon fall below the knot.
        var hang=ornamentPin+weight*new Vector2(.015f*v.X,4);
        return Vector2.Lerp(flat,hang,Band(p.Y,628,1040));
    }
    internal Vector2 Rear(Vector2 p)=>Vector2.Lerp(Project(p,Scalp(p)-80),new Vector2(0,6*weight),Band(p.Y,600,1045));
    internal Vector2 Backing(Vector2 p)=>Vector2.Lerp(Cap(p),Rear(p),Band(p.Y,288,620));
}
