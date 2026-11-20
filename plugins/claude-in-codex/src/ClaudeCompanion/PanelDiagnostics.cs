using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace ClaudeCompanion;

internal static class PanelDiagnostics
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var requests=0;
        var usageTime=TimeSpan.Zero;
        TaskCompletionSource? read=null;
        var window=new CompanionWindow(diagnostic:true,readUsage:()=>{requests++;return read?.Task??Task.CompletedTask;},usageClock:()=>usageTime);
        try
        {
            var snapshot=new UsageSnapshot([new(10080,9,new DateTimeOffset(2026,11,23,10,6,0,TimeSpan.FromHours(8)))],
                new DateTimeOffset(2026,11,16,20,9,3,TimeSpan.FromHours(8)),"已连接 · 预览数据");
            check(!window.DiagnosticOther.IsExpanded,"other settings collapsed by default");
            check(ReferenceEquals(window.DiagnosticTaskStatus.Parent,window.DiagnosticOther.Content),"task-event details belong only to the collapsed other-settings content");
            check(window.DiagnosticTaskStatus.TextWrapping==TextWrapping.Wrap,"task-event details wrap inside the settings panel");
            check(window.DiagnosticPanelRoot.Child==window.DiagnosticScroll,"all panel content inside one scroll container");
            read=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstOpen=window.DiagnosticOpen();
            check(requests==1&&!firstOpen.IsCompleted&&window.DiagnosticIsLoading,"the first open requests immediately and does not render empty quota while waiting");
            await window.DiagnosticOpen();check(requests==1,"opening during the first read does not duplicate the request");
            read.SetResult();await firstOpen;read=null;
            await window.DiagnosticPanel(snapshot,"dark");
            await window.DiagnosticPanel(snapshot,"light");
            check(((SolidColorBrush)window.DiagnosticPanelRoot.Background).Color==Colors.White,"existing panel background actually repaints light, not only the global resource");
            var buttons=Descendants<Button>(window.DiagnosticPanelRoot).ToList();
            check(buttons.All(b=>!AutomationProperties.GetName(b).Contains("刷新")),"no refresh button in the panel");
            check(buttons.Any(b=>AutomationProperties.GetName(b)=="重置到右下角"),"position reset remains outside collapsed settings");
            check(Descendants<RadioButton>(window.DiagnosticPanelRoot).Count()==3,"three themed appearance segments retained");
            await window.DiagnosticPanel(snapshot,"dark",true);
            check(window.DiagnosticMotion.Style!=null&&window.DiagnosticSize.Style!=null,"motion switch and slider use custom styles");
            var track=(Track)window.DiagnosticSize.Template.FindName("PART_Track",window.DiagnosticSize);
            check(track.Thumb?.Style!=null,"slider uses the designed circular thumb");

            await window.DiagnosticPanel(snapshot,"light",true);
            check(window.DiagnosticPanelRoot.ActualHeight<=PanelGeometry.MaximumHeight,"expanded settings respect maximum panel height");
            check(window.DiagnosticScroll.ScrollableHeight>0,"expanded content overflows into scrolling, not window growth");
            var scrollBar=(ScrollBar)window.DiagnosticScroll.Template.FindName("PART_VerticalScrollBar",window.DiagnosticScroll);
            check(scrollBar.Width==8&&scrollBar.Style!=null,"scroll container uses the designed slim scrollbar");
            var scrollTrack=(Track)scrollBar.Template.FindName("PART_Track",scrollBar);
            check(scrollBar.ActualWidth<=8&&scrollTrack.Thumb.ActualWidth<=8&&scrollTrack.Thumb.Template.FindName("Grip",scrollTrack.Thumb)!=null,"rendered scrollbar thumb also uses the slim custom template");
            window.DiagnosticScroll.ScrollToEnd();
            await Idle();
            check(window.DiagnosticScroll.VerticalOffset>0,"bottom controls reachable through scrolling");
            var offset=window.DiagnosticScroll.VerticalOffset;
            var panel=window.DiagnosticPanelRoot;var launcher=Descendants<Button>(window.DiagnosticLauncher).Single();
            var point=window.DiagnosticPanelPoint(panel.RenderSize);
            window.DiagnosticSettings.OffsetX=47;window.DiagnosticSettings.OffsetY=23;
            window.DiagnosticTheme("dark");await Idle();
            check(ReferenceEquals(panel,window.DiagnosticPanelRoot)&&ReferenceEquals(launcher,Descendants<Button>(window.DiagnosticLauncher).Single()),"theme change does not rebuild panel or launcher");
            check(window.DiagnosticOther.IsExpanded,"theme change retains expanded state");
            check(Math.Abs(offset-window.DiagnosticScroll.VerticalOffset)<0.1,"theme change retains scroll offset");
            check(window.DiagnosticPanelPoint(panel.RenderSize)==point,"theme change retains the panel bottom-relative anchor");
            check(window.DiagnosticSettings.OffsetX==47&&window.DiagnosticSettings.OffsetY==23,"theme change preserves dragged companion offsets");
            check(((SolidColorBrush)Application.Current.FindResource("PanelBrush")).Color==Color.FromRgb(32,32,34),"existing controls receive dark resources without reload");
            window.DiagnosticMotion.IsChecked=true;
            check(window.DiagnosticSettings.ReducedMotion,"designed motion switch updates preference");
            window.DiagnosticSize.Value=192;
            check(window.DiagnosticSettings.Size==192,"designed slider updates character size");
            await window.DiagnosticOpen();await window.DiagnosticOpen();
            check(requests==1,"later panel openings display cached quota without starting another read");
            check(!window.DiagnosticOther.IsExpanded,"reopening defaults other settings to collapsed");
            await Idle();check(window.DiagnosticScroll.VerticalOffset==0,"reopening starts at top of the panel");

            var many=new UsageSnapshot(Enumerable.Range(1,6).Select(i=>new QuotaWindow(300*i,i*13,null)).ToList(),null,"已连接 · 预览数据");
            window.DiagnosticPanelAnchor(new Size(640,420),new Rect(424,184,192,192));
            await window.DiagnosticPanel(many,"dark",true,"panel-short-host.png");
            check(window.DiagnosticPanelRoot.ActualHeight<=164&&window.DiagnosticScroll.ScrollableHeight>0,"short host constrains the complete scrollable panel");
            window.DiagnosticPanelAnchor(new Size(256,720),new Rect(96,500,144,144));
            await window.DiagnosticPanel(snapshot,"light",true,"panel-narrow.png");
            check(window.DiagnosticPanelRoot.ActualWidth==240&&window.DiagnosticScroll.ExtentWidth<=window.DiagnosticScroll.ViewportWidth+1,"narrow panel content fits without horizontal overflow");

            var moment=DateTimeOffset.UtcNow;
            var events=new TaskEvents();
            var turns=Enumerable.Range(1,3).Select(i=>new TaskSignal(TaskEvents.HashIdentifier($"session{i}"),TaskEvents.HashIdentifier($"turn{i}"),"UserPromptSubmit",moment.UtcTicks)).ToArray();
            foreach(var turn in turns)events.Accept(turn);
            var child=turns[0] with{Event="SubagentStop",Agent=TaskEvents.HashIdentifier("child"),Ticks=moment.AddMilliseconds(10).UtcTicks};
            events.Accept(child);await window.DiagnosticTasks(events.Snapshot(moment));
            check(requests==1,"task starts and child completions do not refresh quota");
            read=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish=turns[0] with{Event="Stop",Ticks=moment.AddMilliseconds(30).UtcTicks};
            events.Accept(finish);
            var completionRead=window.DiagnosticTasks(events.Snapshot(moment));
            check(requests==2&&!completionRead.IsCompleted,"a main completion starts a read even if the menu is closed");
            events.Accept(finish);
            foreach(var turn in turns.Skip(1))events.Accept(turn with{Event="Stop",Ticks=finish.Ticks});
            var queuedReads=window.DiagnosticTasks(events.Snapshot(moment));
            await window.DiagnosticOpen();
            check(requests==2&&!queuedReads.IsCompleted,"opening during a background read uses cache without adding another read");
            read.SetResult();await queuedReads;read=null;
            await window.DiagnosticTasks(events.Snapshot(moment));
            check(requests==4,"two more completions in one poll each read quota; duplicates and empty polls do not");

            usageTime=TimeSpan.FromSeconds(599);
            await window.DiagnosticUsageFallback();
            check(requests==4,"the timer does not read quota before ten minutes");
            usageTime+=TimeSpan.FromSeconds(1);
            read=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var fallbackRead=window.DiagnosticUsageFallback();
            await window.DiagnosticUsageFallback();
            check(requests==5&&!fallbackRead.IsCompleted,"ten idle minutes start one background read without stacking another");
            read.SetResult();await fallbackRead;read=null;
            usageTime+=TimeSpan.FromMinutes(9);
            await window.DiagnosticUsageFallback();
            check(requests==5,"a periodic attempt restarts the ten-minute interval");
            var nextTurn=turns[0] with{Turn=TaskEvents.HashIdentifier("later"),Ticks=moment.AddMilliseconds(40).UtcTicks};
            events.Accept(nextTurn);events.Accept(nextTurn with{Event="Stop",Ticks=moment.AddMilliseconds(50).UtcTicks});
            await window.DiagnosticTasks(events.Snapshot(moment));
            usageTime+=TimeSpan.FromMinutes(1);
            await window.DiagnosticUsageFallback();
            check(requests==6,"a new main completion refreshes immediately and postpones the fallback");
            usageTime+=TimeSpan.FromMinutes(9);
            await window.DiagnosticUsageFallback();
            usageTime+=TimeSpan.FromMinutes(10);
            await window.DiagnosticUsageFallback();
            check(requests==8,"the timer refreshes every ten quiet minutes even if the menu is not opened again");

            var nearest=moment.AddDays(2);var later=moment.AddDays(8);
            var withCredits=snapshot with{ResetCredits=new(3,[new(later),new(nearest)])};
            var creditSection=window.DiagnosticCreditSection;
            var expiry=window.DiagnosticCreditExpiry;
            check(window.DiagnosticSettings.ResetCreditReminder&&window.DiagnosticReminderDays.Value==7,"reset-credit reminder defaults to one week");
            await window.DiagnosticPanel(withCredits,"dark",filename:"panel-nearest-expiry-narrow.png");
            check(window.DiagnosticScroll.ExtentWidth<=window.DiagnosticScroll.ViewportWidth+1,"nearest expiry fits the narrow panel without horizontal overflow");
            window.DiagnosticPanelAnchor(new Size(1280,800),new Rect(1000,556,224,224));
            await window.DiagnosticPanel(withCredits,"dark",filename:"panel-nearest-expiry.png");
            check(window.DiagnosticRibbon&&!creditSection.IsExpanded&&window.DiagnosticCreditSummary.Text=="重置券 · 3 张"&&window.DiagnosticCreditSoon.Text=="1 张临期","credit count is collapsed and one expiring credit enables the ribbon");
            check(expiry.Visibility==Visibility.Visible&&expiry.Text==$"最近 {nearest.ToLocalTime():MM-dd HH:mm} 到期","the collapsed heading displays the closest usable deadline even when the list is unsorted");
            var heading=(WrapPanel)creditSection.Header;
            var countPos=window.DiagnosticCreditSummary.TranslatePoint(new Point(0,window.DiagnosticCreditSummary.ActualHeight/2),heading);
            var expiryPos=expiry.TranslatePoint(new Point(0,expiry.ActualHeight/2),heading);
            check(expiryPos.X>countPos.X&&Math.Abs(expiryPos.Y-countPos.Y)<1,"at normal width, the nearest deadline is on the same row after the count");
            creditSection.IsExpanded=true;
            check(((StackPanel)creditSection.Content).Children.OfType<TextBlock>().Any(t=>t.Text.Contains("1 张未返回到期明细")),"partial credit list labels the missing details");
            window.DiagnosticReminderDays.Value=1;
            check(!window.DiagnosticRibbon,"shortening the threshold can hide the stamp immediately");
            window.DiagnosticReminderDays.Value=10;
            check(window.DiagnosticRibbon,"adjusting the threshold rechecks the ribbon without another read");
            window.DiagnosticReminderSwitch.IsChecked=false;
            check(!window.DiagnosticRibbon&&!window.DiagnosticReminderDays.IsEnabled,"disabling the reminder hides the stamp but not the details");
            window.DiagnosticReminderSwitch.IsChecked=true;window.DiagnosticReminderDays.Value=7;
            window.DiagnosticResetCredits(withCredits,moment.AddDays(2));
            check(expiry.Text==$"最近 {later.ToLocalTime():MM-dd HH:mm} 到期","the cached heading advances at expiry without another request");
            window.DiagnosticResetCredits(withCredits with{Stale=true},moment.AddDays(9));
            check(!window.DiagnosticRibbon&&expiry.Visibility==Visibility.Collapsed,"expired cached credits leave neither a stamp nor a deadline");
            window.DiagnosticResetCredits(withCredits with{Stale=true},moment);
            check(!window.DiagnosticRibbon,"stale details cannot activate a new reminder");
            window.DiagnosticResetCredits(withCredits with{ResetCredits=new(3,null)},moment);
            check(!window.DiagnosticRibbon&&window.DiagnosticCreditSummary.Text=="重置券 · 3 张"&&expiry.Visibility==Visibility.Collapsed,"count-only response does not infer a deadline");
            window.DiagnosticResetCredits(withCredits with{ResetCredits=new(0,[])},moment);
            check(!window.DiagnosticRibbon&&window.DiagnosticCreditSummary.Text=="重置券 · 0 张","authoritative zero can be shown without a stamp");

            foreach(var dpi in new[]{1d,1.25,1.5,2})
            {
                var settings=new Settings{Size=192,OffsetX=47,OffsetY=23,PersistenceEnabled=false};
                var physical=Native.Place(new Native.Rect{Right=1920,Bottom=1080},-1920,0,dpi,settings);
                var host=new Size(1920/dpi,1080/dpi);
                var pet=new Rect((physical.X+1920)/dpi,physical.Y/dpi,physical.Size/dpi,physical.Size/dpi);
                var constraint=PanelGeometry.Constrain(host,pet);
                var small=PanelGeometry.Place(new Size(constraint.Width,Math.Min(240,constraint.MaxHeight)),host,pet,constraint.Above);
                var large=PanelGeometry.Place(new Size(constraint.Width,constraint.MaxHeight),host,pet,constraint.Above);
                check(Math.Abs(small.Y+Math.Min(240,constraint.MaxHeight)+PanelGeometry.Gap)<0.001&&Math.Abs(large.Y+constraint.MaxHeight+PanelGeometry.Gap)<0.001,$"panel bottom stays anchored across content heights at DPI {dpi}");
                check(large.Y+pet.Top>=7.99&&large.X+pet.Left>=7.99,$"panel stays inside host client area at DPI {dpi}");
            }
            var topPet=new Rect(20,16,128,128);var topHost=new Size(800,600);var below=PanelGeometry.Constrain(topHost,topPet);
            var fallback=PanelGeometry.Place(new Size(below.Width,below.MaxHeight),topHost,topPet,below.Above);
            check(!below.Above&&fallback.Y==140,"top-edge pet uses stable below-anchor fallback");
            var tinyHost=new Size(320,200);var fullHeightPet=new Rect(112,0,200,200);var tiny=PanelGeometry.Constrain(tinyHost,fullHeightPet);
            var overlay=PanelGeometry.Place(new Size(tiny.Width,tiny.MaxHeight),tinyHost,fullHeightPet,tiny.Above);
            check(tiny.MaxHeight==184&&overlay.Y==8,"pet filling a short host still gets a usable scrollable overlay");
            SavePreview();
        }
        finally{window.DisposeForDiagnostics();}
    }
    private static void SavePreview()
    {
        var output=Environment.GetEnvironmentVariable("CLAUDE_TEST_OUTPUT")??Path.Combine(Settings.DirectoryPath,"diagnostics");
        var left=PetView.Load(Path.Combine(output,"panel-dark.png"));var right=PetView.Load(Path.Combine(output,"panel-light-expanded.png"));
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22,22,24)),null,new Rect(0,0,688,530));
            void Label(string text,double x)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.LightGray,1),new Point(x,9));
            Label("DEFAULT / DARK",16);Label("OTHER SETTINGS / LIGHT",352);
            dc.DrawImage(left,new Rect(16,32,left.PixelWidth,left.PixelHeight));dc.DrawImage(right,new Rect(352,32,right.PixelWidth,right.PixelHeight));
        }
        var bitmap=new RenderTargetBitmap(688,530,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);Diagnostics.Save(bitmap,"panel-redesign.png");
    }
    private static async Task Idle()=>await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match)yield return match;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var item in Descendants<T>(child))yield return item;
    }
}
