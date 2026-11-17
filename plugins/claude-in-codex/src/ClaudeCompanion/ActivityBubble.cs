using System.Windows;
using System.Windows.Media;

namespace ClaudeCompanion;

// Foreground overlay in WPF, deliberately outside the character's skinning and supersampling passes.
internal static class ActivityBubble
{
    internal static void Draw(DrawingContext dc,Point origin,double clock,double opacity,Brush background,string state="thinking")
    {
        if(opacity<.001)return;
        var x=origin.X;var y=origin.Y;
        var accent=new SolidColorBrush(Color.FromRgb(255,140,43));
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(background,new Pen(accent,1),new Rect(x,y,34,19),8,8);
        if(state=="complete")
        {
            var check=new StreamGeometry();using(var path=check.Open())
            {path.BeginFigure(new Point(x+11,y+9),false,false);path.LineTo(new Point(x+15,y+13),true,false);path.LineTo(new Point(x+23,y+6),true,false);}
            check.Freeze();dc.DrawGeometry(null,new Pen(accent,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},check);
        }
        else for(var i=0;i<3;i++)
        {
            dc.PushOpacity(.3+.7*(.5+.5*Math.Sin(clock*5-i*1.5)));
            dc.DrawEllipse(accent,null,new Point(x+10+i*7,y+9),1.8,1.8);dc.Pop();
        }
        dc.DrawEllipse(accent,null,new Point(x+29,y+23),1.4,1.4);dc.Pop();
    }
}
