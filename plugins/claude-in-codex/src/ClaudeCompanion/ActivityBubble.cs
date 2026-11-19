using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace ClaudeCompanion;

// Foreground overlay in WPF, deliberately outside the character's skinning and supersampling passes.
internal static class ActivityBubble
{
    internal static double Width(string title)=>string.IsNullOrWhiteSpace(title)?34:112;
    internal static double Width(TaskBubbleFrame frame)=>Width(frame.Title)+(frame.Children>0?30:0);
    internal static string Elapsed(TaskBubbleFrame f,DateTimeOffset now){
        if(f.StartedTicks<=0)return "";
        var t=TimeSpan.FromTicks(Math.Max(0,(f.EndTicks>0?f.EndTicks:now.UtcTicks)-f.StartedTicks));
        return $"耗时 {(int)t.TotalMinutes}:{t.Seconds:00}";
    }
    internal static string Label(string title)
    {
        var elements=StringInfo.GetTextElementEnumerator(title.Replace('\r',' ').Replace('\n',' ').Replace('\t',' '));var result=new List<string>();
        while(elements.MoveNext()){if(result.Count==6)return string.Concat(result)+"...";result.Add(elements.GetTextElement());}
        return string.Concat(result);
    }
    internal static IReadOnlyList<(TaskBubbleFrame Frame,Rect Bounds)> Layout(IReadOnlyList<TaskBubbleFrame> frames,Rect area)
    {
        if(frames.Count==0||area.Width<=0||area.Height<=0)return [];
        var width=frames.Max(Width);var columns=width>34?Math.Max(1,(int)(area.Width/(width+8))):3;
        var rows=Math.Max(2,(frames.Max(f=>Math.Max(f.Capacity,f.Slot+1))+columns-1)/columns);var w=area.Width/columns;var h=area.Height/rows;
        var scale=Math.Min(1,Math.Min(w/(width+4),h/29));
        return frames.Select(f=>
        {
            var x=area.X+(f.Slot%columns)*w+(w-Width(f)*scale)*(.45+.2*Math.Sin(f.Slot*2.1));
            var y=area.Y+(f.Slot/columns)*h+(h-25*scale)*(.5+.25*Math.Cos(f.Slot*2.7));
            return (f,new Rect(x,y,Width(f)*scale,25*scale));
        }).ToArray();
    }
    internal static void Draw(DrawingContext dc,Point origin,double clock,double opacity,Brush background,string state="thinking",double progress=1,string title="",int children=0)
    {
        if(opacity<.001)return;
        var x=origin.X;var y=origin.Y;
        var accent=new SolidColorBrush(Color.FromRgb(255,140,43));
        dc.PushOpacity(opacity);
        var width=Width(title)+(children>0?30:0);
        dc.DrawRoundedRectangle(background,new Pen(accent,1),new Rect(x,y,width,19),8,8);
        if(!string.IsNullOrWhiteSpace(title))
        {
            var text=new FormattedText(Label(title),CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10.5,accent,1)
                {MaxTextWidth=74,MaxTextHeight=17,Trimming=TextTrimming.CharacterEllipsis};
            dc.DrawText(text,new Point(x+7,y+2));x+=Width(title)-34;
        }
        if(children>0){
            var count=children>99?"×99+":"×"+children;
            dc.DrawText(new FormattedText(count,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),10,accent,1),new Point(x+6,y+2));
            x+=30;
        }
        if(state=="complete")
        {
            var t=Math.Clamp(progress*5,0,1);var check=new StreamGeometry();using(var path=check.Open())
            {
                path.BeginFigure(new Point(x+11,y+9),false,false);
                var first=Math.Min(1,t/.35);path.LineTo(new Point(x+11+4*first,y+9+4*first),true,false);
                if(t>.35){var last=(t-.35)/.65;path.LineTo(new Point(x+15+8*last,y+13-7*last),true,false);}
            }
            check.Freeze();dc.DrawGeometry(null,new Pen(accent,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},check);
        }
        else if(state=="compact"){
            var pen=new Pen(accent,1.4);
            dc.DrawRoundedRectangle(null,pen,new Rect(x+10,y+4,10,7),1,1);
            dc.DrawRoundedRectangle(background,pen,new Rect(x+13,y+7,10,7),1,1);
        }
        else for(var i=0;i<3;i++)
        {
            dc.PushOpacity(.3+.7*(.5+.5*Math.Sin(clock*5-i*1.5)));
            dc.DrawEllipse(accent,null,new Point(x+10+i*7,y+9),1.8,1.8);dc.Pop();
        }
        dc.DrawEllipse(accent,null,new Point(x+29,y+23),1.4,1.4);dc.Pop();
    }
    internal static void DrawElapsed(DrawingContext dc,string label,Rect anchor,Rect bounds,Brush background){
        if(label.Length==0)return;
        var accent=new SolidColorBrush(Color.FromRgb(255,140,43));
        var text=new FormattedText(label,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,accent,1);
        double w=text.Width+14,h=text.Height+5;
        double x=Math.Clamp(anchor.Right-w,bounds.Left,Math.Max(bounds.Left,bounds.Right-w));
        double y=anchor.Top>=bounds.Top+h+2?anchor.Top-h-2:Math.Min(anchor.Bottom+1,bounds.Bottom-h);
        dc.DrawRoundedRectangle(background,new Pen(accent,.8),new Rect(x,y,w,h),5,5);dc.DrawText(text,new Point(x+7,y+2));
    }
}
