using SkiaSharp;

namespace MotionLab;

internal sealed partial class Renderer
{
    internal string StudyId="";
    internal double StudyTime;
    internal float Drop;
    static readonly SKColor Cocoa=new(72,37,39);
    partial void DrawStudyCels(SKCanvas c,Pose p)
    {
        if(StudyId=="")return;
        float B(double a,double b,double d,double e)=>Acting.Beat(StudyTime,a,b,d,e);
        var weight=StudyId switch{"thinking"=>B(1.58,1.80,3.65,3.95),"pat"=>B(.38,.76,3.66,4.53),
            "sleepy"=>B(1.44,2.48,3.1,3.35),"wink"=>B(.13,.29,.73,1.14),_=>0};
        if(weight<.001)return;
        using var opacity=new SKPaint{Color=SKColors.White.WithAlpha((byte)(255*weight))};
        c.SaveLayer(opacity);
        using var fill=new SKPaint{IsAntialias=true};
        void Shape(string svg,SKColor color,float edge=0,SKColor? outline=null)
        {
            using var path=SKPath.ParseSvgPathData(svg);
            fill.Style=SKPaintStyle.Fill;fill.Color=color;c.DrawPath(path,fill);
            if(edge<=0)return;
            fill.Style=SKPaintStyle.Stroke;fill.StrokeJoin=SKStrokeJoin.Round;fill.StrokeWidth=edge;
            fill.Color=outline??Cocoa;c.DrawPath(path,fill);
        }
        void Clean(SKRect patch){
            c.Save();c.ClipRect(patch);DrawPart(c,"face",paint);c.Restore();
        }
        if(StudyId=="thinking"){
            Clean(new(274,716,980,920));Clean(new(550,928,712,1013));
            // Mirrored outer flicks reuse the length and weight of the neutral upper lash.
            foreach(bool right in new[]{false,true}){
                c.Save();if(right){c.Translate(1258,3);c.Scale(-1,1);}
                Shape("M325 772 Q380 752 473 758 Q533 759 540 783 Q544 833 500 848 Q428 866 363 846 Q330 837 325 803 Z",new(255,248,232),9);
                Shape("M278 747 Q306 762 337 759 Q441 743 515 756 Q537 760 541 777 L540 788 Q519 775 483 774 Q400 769 345 788 Q327 797 325 809 Q304 793 278 747Z",Ink);
                c.Restore();
            }
            Shape("M566 963 Q570 943 589 948 Q613 955 635 949 Q661 943 682 949 Q696 954 691 971 Q686 982 665 977 Q628 971 595 980 Q572 983 566 970Z",new(156,84,78),5);
            Shape("M589 973 Q618 962 650 967 Q662 969 674 974 Q638 971 598 978 Z",new(231,149,130));
            foreach(var (x,k,w) in new[]{(587, .78f, Drop),(671,1f,B(2.10,3.20,3.58,3.89))}){
                if(w<.01)continue;
                c.Save();c.Translate(x,974);c.Scale(.8f+.2f*w,k*w);
                Shape("M-9 0 Q-1 -4 10 0 Q7 14 11 31 Q17 51 5 54 Q-7 56 -8 42 Q-7 23 -9 0Z",new(174,219,220),3,new(102,109,114));
                Shape("M-4 5 Q-1 3 1 5 L2 26 Q1 32 -2 29Z",new(249,255,246));c.Restore();
            }
        }else if(StudyId=="pat"){
            foreach(int x in new[]{344,846})for(int i=0;i<3;i++){
                fill.Style=SKPaintStyle.Stroke;fill.StrokeWidth=5;fill.StrokeCap=SKStrokeCap.Round;fill.Color=new(208,126,120);
                c.DrawLine(x+i*16,885-i%2*4,x+i*16+7,872-i%2*4,fill);
            }
        }else if(StudyId=="sleepy"){
            Clean(new(575,931,698,988));
            Shape("M611 961 Q617 949 631 950 Q653 950 655 967 Q652 980 642 981 Q625 983 622 973 Q618 966 611 965Z",new(132,76,72),4);
        }else if(StudyId=="wink"){
            c.Save();c.Translate(459+p.GazeX*18,829);c.Scale(.72f+.28f*weight);
            Shape("M0 -21 Q4 -4 18 0 Q4 3 0 21 Q-3 5 -18 0 Q-4 -3 0 -21Z",new(255,242,202));c.Restore();
        }
        c.Restore();
    }
}
