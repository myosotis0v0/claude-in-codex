using SkiaSharp;

namespace MotionLab;

internal sealed partial class Renderer
{
    private const double RaiseStart=.52,RaiseDuration=.64;
    private const double FullyRaisedAt=RaiseStart+RaiseDuration,HoldSeconds=1.5,ReturnRate=1.28;
    internal const double PlacardSeconds=FullyRaisedAt+HoldSeconds+FullyRaisedAt/ReturnRate;
    internal const int PlacardLeftInset=177;
    private SKPicture? placard;
    private void DrawPlacardHand(SKCanvas c,double t,float mouse,float pointer,float typingY)
    {
        static float Smooth(double t){var a=(float)Math.Clamp(t,0,1);return a*a*(3-2*a);}
        // Run the same pickup backwards on return. Change grip only below the crop.
        var phase=t<=FullyRaisedAt?t:Math.Clamp((PlacardSeconds-t)*ReturnRate,0,FullyRaisedAt);
        var dip=Smooth((phase-.14)/.34);
        var raise=Smooth((phase-RaiseStart)/RaiseDuration);
        var carrying=phase>=.51;
        var arc=4*raise*(1-raise);
        var wx=318+96*dip-85*raise+12*arc;
        var wy=1170+365*dip-365*raise-18*arc;
        var wrist=carrying?204-105*raise:-25*dip;
        c.Save();c.Translate(pointer+(mouse-pointer)*dip+wx,wy+typingY*(1-dip));c.RotateDegrees(wrist);c.Translate(-318,-1170);
        if(carrying){
            const float gripX=308,gripY=1201;
            // The paddle and the tucked hand share one wrist matrix, with no separate angle track.
            c.Save();c.Translate(gripX,gripY);c.RotateDegrees(-111);
            c.DrawPicture(placard??=RecordPlacard());
            c.Restore();
            c.RotateDegrees(180,gripX,gripY);
        }
        DrawPart(c,"mouse-paw",paint);
        c.Restore();
    }

    private static SKPicture RecordPlacard()
    {
        using var recorder=new SKPictureRecorder();
        var c=recorder.BeginRecording(new SKRect(-220,-580,220,80));
        using var ink=new SKPaint{IsAntialias=true,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
        void Stroke(string col,float w){ink.Color=SKColor.Parse(col);ink.Style=SKPaintStyle.Stroke;ink.StrokeWidth=w;}
        void Fill(string col){ink.Color=SKColor.Parse(col);ink.Style=SKPaintStyle.Fill;}
        const string outline="#442730";
        Stroke(outline,35);c.DrawLine(0,54,0,-402,ink);
        Stroke("#dfa875",20);c.DrawLine(0,54,0,-402,ink);
        c.Save();
        c.Translate(0,-402);c.Scale(2.03f);
        var front=new SKRect(-97,-75,97,75);
        var edge=new SKRect(-97,-68,97,82);
        Fill("#d79a69");c.DrawRoundRect(edge,23,23,ink);Stroke(outline,8);c.DrawRoundRect(edge,23,23,ink);
        Fill("#fff0d9");c.DrawRoundRect(front,23,23,ink);Stroke(outline,8);c.DrawRoundRect(front,23,23,ink);
        using var path=new SKPathBuilder();path.MoveTo(-42,0);path.LineTo(-12,29);path.LineTo(45,-32);
        using var tick=path.Detach();Stroke("#398356",19);c.DrawPath(tick,ink);c.Restore();
        return recorder.EndRecording();
    }
}
