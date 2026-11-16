using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace ClaudeCompanion;

public sealed class CompanionWindow : Window
{
    private readonly Settings settings;
    private readonly Func<HashSet<int>> discover;
    private readonly Func<HashSet<int>,nint> findTarget;
    private readonly bool diagnostic;
    private readonly PluginLifetime pluginLifetime=new();
    private readonly Grid root=new();
    private Button launcher=null!;
    private bool expanded;
    private readonly Settings launcherPosition=new(){Size=36,OffsetX=0,OffsetY=0,PersistenceEnabled=false};
    private readonly ThemeManager theme=new();
    private readonly UsageClient usage=new();
    private readonly PetView pet=new();
    private readonly DispatcherTimer tracker=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DispatcherTimer gazeTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromMinutes(2)};
    private readonly Popup panel=new(){AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Top,PopupAnimation=PopupAnimation.Fade};
    private StackPanel quotas=new();
    private TextBlock status=new(),updated=new();
    private Button refresh=null!,lockButton=null!,closeButton=null!;
    private nint handle,target;
    private HashSet<int> pids=[];
    private DateTime lastDiscovery=DateTime.MinValue;
    private bool seenTarget,dragging,moved,closed;
    private Native.Point pointerStart;
    private int windowStartX,windowStartY,currentX,currentY,currentSize;
    private double targetScale=1;
    public CompanionWindow(Func<HashSet<int>>? discover=null,Func<HashSet<int>,nint>? findTarget=null,bool diagnostic=false)
    {
        this.discover=discover??Native.CodexPids;this.findTarget=findTarget??Native.FindWindow;this.diagnostic=diagnostic;
        settings=diagnostic?new Settings{PersistenceEnabled=false}:Settings.Load();
        Title="Claude in Codex";Width=settings.Size;Height=settings.Size;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=false;
        root.Children.Add(pet);pet.Visibility=Visibility.Collapsed;
        Content=root;Opacity=0;
        pet.Cursor=settings.Locked?Cursors.Hand:Cursors.SizeAll;
        pet.ToolTip="用量与设置";
        if(!diagnostic)Settings.Log("Startup stage=artwork-ready");
        theme.Apply(settings.Theme);
        panel.PlacementTarget=this;panel.Child=BuildPanel();BuildLauncher();
        theme.Applied+=()=>{panel.Child=BuildPanel();BuildLauncher();UpdateUsage(usage.Current);};
        SourceInitialized+=(_,_)=>
        {
            handle=new WindowInteropHelper(this).Handle;
            Native.SetStyle(handle,-20,Native.GetStyle(handle,-20)|0x80|0x08000000);
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
            if(!diagnostic)Settings.Log("Startup stage=window-handle-ready");
        };
        ContentRendered+=(_,_)=>{if(!diagnostic)Settings.Log($"Startup stage=content-rendered size={ActualWidth:0.#}x{ActualHeight:0.#}");};
        Loaded+=(_,_)=>{Follow();tracker.Start();};
        pet.MouseLeftButtonDown+=Pressed;pet.MouseMove+=Dragged;pet.MouseLeftButtonUp+=Released;
        pet.MouseRightButtonUp+=(_,e)=>{TogglePanel();e.Handled=true;};
        pet.LostMouseCapture+=(_,_)=>{if(dragging){dragging=false;SavePosition();}};
        tracker.Tick+=(_,_)=>Follow();
        gazeTimer.Tick+=(_,_)=>Look();
        refreshTimer.Tick+=async(_,_)=>{if(IsVisible)await usage.RefreshAsync();};
        usage.Changed+=snapshot=>Dispatcher.BeginInvoke(()=>UpdateUsage(snapshot));
        Closed+=(_,_)=>Cleanup();
    }
    private nint WndProc(nint hwnd,int msg,nint w,nint l,ref bool handled)
    {
        if(msg==0x21){handled=true;return 3;} // MA_NOACTIVATE
        if(msg==0x84 && expanded && !dragging)
        {
            var x=(short)(l.ToInt64()&0xffff);var y=(short)((l.ToInt64()>>16)&0xffff);
            var p=pet.PointFromScreen(new Point(x,y));
            if(!pet.Hit(p.X,p.Y)){handled=true;return -1;}
        }
        return 0;
    }
    private void Follow()
    {
        if(closed)return;
        if((DateTime.UtcNow-lastDiscovery).TotalSeconds>2)
        {
            if(!diagnostic&&pluginLifetime.ShouldExit()){Close();return;}
            pids=discover();lastDiscovery=DateTime.UtcNow;
            if(seenTarget&&pids.Count==0){Close();return;}
            var candidate=findTarget(pids);
            if(candidate!=0 && candidate!=target)
            {
                target=candidate;new WindowInteropHelper(this).Owner=target;
                if(!diagnostic)Settings.Log("Startup stage=codex-window-attached");
            }
        }
        if(target==0||!Native.IsWindow(target)||!Native.IsWindowVisible(target)||Native.IsIconic(target)||Native.Cloaked(target))
        {
            panel.IsOpen=false;gazeTimer.Stop();refreshTimer.Stop();
            if(IsVisible)Hide();
            return;
        }
        seenTarget=true;
        if(!Native.GetClientRect(target,out var client))return;
        var origin=new Native.Point();Native.ClientToScreen(target,ref origin);
        if(client.Width<=0||client.Height<=0)return;
        targetScale=Math.Max(1,Native.GetDpiForWindow(target))/96d;
        var placement=Native.Place(client,origin.X,origin.Y,targetScale,expanded?settings:launcherPosition);
        if(!dragging)
        {
            var changed=currentX!=placement.X||currentY!=placement.Y||currentSize!=placement.Size;
            currentX=placement.X;currentY=placement.Y;currentSize=placement.Size;
            if(changed)
            {
                Native.SetWindowPos(handle,0,currentX,currentY,currentSize,currentSize,0x14);
                if(panel.IsOpen){panel.HorizontalOffset=0.01;panel.HorizontalOffset=0;}
            }
        }
        if(!IsVisible){Show();_=usage.RefreshAsync();}
        if(Opacity<1&&!diagnostic)Settings.Log($"Startup stage=launcher-visible dpi={targetScale:0.##} bounds={currentX},{currentY},{currentSize}");
        Opacity=1;
        if(expanded&&!gazeTimer.IsEnabled)gazeTimer.Start();
        if(!expanded)gazeTimer.Stop();
        if(!refreshTimer.IsEnabled){refreshTimer.Start();_=usage.RefreshAsync();}
    }
    private void Look()
    {
        if(!Native.GetCursorPos(out var cursor))return;
        var centerX=currentX+currentSize*0.5;var centerY=currentY+currentSize*0.65;
        var range=Math.Max(240,360*targetScale);
        pet.Look(new Vector((cursor.X-centerX)/range,(cursor.Y-centerY)/range),settings.ReducedMotion);
    }
    private void BuildLauncher()
    {
        if(launcher!=null)root.Children.Remove(launcher);
        var image=new System.Windows.Controls.Image{Source=new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory,"assets","icon-64.png"))),Width=28,Height=28};
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
        launcher=new Button{Content=image,Padding=new Thickness(3),ToolTip="Claude · 展开桌宠与用量",Margin=new Thickness(0)};
        launcher.SetResourceReference(Button.BackgroundProperty,"PanelBrush");
        launcher.SetResourceReference(Button.BorderBrushProperty,"AccentBrush");
        launcher.Click+=(_,_)=>Expand();
        launcher.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;root.Children.Add(launcher);
    }
    public void Expand()
    {
        expanded=true;pet.Visibility=Visibility.Visible;launcher.Visibility=Visibility.Collapsed;
        if(!diagnostic){Follow();if(IsVisible&&!panel.IsOpen)TogglePanel();}
    }
    private void Collapse()
    {
        panel.IsOpen=false;dragging=false;pet.ReleaseMouseCapture();expanded=false;
        pet.Visibility=Visibility.Collapsed;launcher.Visibility=Visibility.Visible;
        if(!diagnostic)Follow();
    }
    private void Pressed(object sender,MouseButtonEventArgs e)
    {
        moved=false;
        if(!settings.Locked && Native.GetCursorPos(out pointerStart))
        {
            dragging=true;windowStartX=currentX;windowStartY=currentY;pet.CaptureMouse();panel.IsOpen=false;
        }
        e.Handled=true;
    }
    private void Dragged(object sender,MouseEventArgs e)
    {
        if(!dragging||!Native.GetCursorPos(out var point))return;
        var dx=point.X-pointerStart.X;var dy=point.Y-pointerStart.Y;
        if(!moved&&Math.Abs(dx)+Math.Abs(dy)<5*targetScale)return;
        moved=true;
        if(!Native.GetClientRect(target,out var client))return;
        var origin=new Native.Point();Native.ClientToScreen(target,ref origin);
        currentX=Math.Clamp(windowStartX+dx,origin.X,origin.X+Math.Max(0,client.Width-currentSize));
        currentY=Math.Clamp(windowStartY+dy,origin.Y,origin.Y+Math.Max(0,client.Height-currentSize));
        Native.SetWindowPos(handle,0,currentX,currentY,currentSize,currentSize,0x14);
    }
    private void Released(object sender,MouseButtonEventArgs e)
    {
        var wasDragging=dragging;dragging=false;pet.ReleaseMouseCapture();
        if(wasDragging&&moved)SavePosition();
        if(!moved)TogglePanel();
        e.Handled=true;
    }
    private void SavePosition()
    {
        if(!Native.GetClientRect(target,out var rect))return;
        var origin=new Native.Point();Native.ClientToScreen(target,ref origin);
        settings.OffsetX=(origin.X+rect.Width-currentX-currentSize)/targetScale;
        settings.OffsetY=(origin.Y+rect.Height-currentY-currentSize)/targetScale;
        settings.Save();
    }
    private void TogglePanel()
    {
        if(!IsVisible)return;
        panel.IsOpen=!panel.IsOpen;
        if(panel.IsOpen){UpdateUsage(usage.Current);if(usage.Current.UpdatedAt is null||usage.Current.Stale)_=Refresh();}
    }
    private static TextBlock Text(string value,int size=13,string? resource=null)
    {
        var text=new TextBlock{Text=value,FontSize=size};
        if(resource!=null)text.SetResourceReference(TextBlock.ForegroundProperty,resource);
        return text;
    }
    private new static Button Icon(string glyph,string tooltip,RoutedEventHandler action)
    {
        var button=new Button{Content=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15},ToolTip=tooltip,Width=32,Height=32,Padding=new Thickness(4),Margin=new Thickness(3,0,0,0)};
        button.Click+=action;return button;
    }
    private static Border Divider()
    {
        var divider=new Border{Height=1,Margin=new Thickness(0,12,0,12)};
        divider.SetResourceReference(Border.BackgroundProperty,"BorderBrush");return divider;
    }
    private UIElement BuildPanel()
    {
        quotas=new StackPanel();status=new TextBlock();updated=new TextBlock();
        var body=new StackPanel{Margin=new Thickness(16)};
        var header=new DockPanel();
        var commands=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(commands,Dock.Right);
        refresh=Icon("\uE72C","刷新额度",async(_,_)=>await Refresh());
        closeButton=Icon("\uE711","关闭桌宠，保留右下角入口",(_,_)=>Collapse());
        commands.Children.Add(refresh);commands.Children.Add(closeButton);header.Children.Add(commands);
        header.Children.Add(Text("Claude",20,"AccentBrush"));body.Children.Add(header);
        status.FontSize=12;status.Margin=new Thickness(0,8,0,12);status.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");body.Children.Add(status);
        body.Children.Add(quotas);
        updated.FontSize=11;updated.Margin=new Thickness(0,8,0,0);updated.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");body.Children.Add(updated);
        body.Children.Add(Divider());
        var position=new DockPanel();
        lockButton=Icon(settings.Locked?"\uE72E":"\uE785",settings.Locked?"解锁拖动":"锁定位置",(_,_)=>
        {
            settings.Locked=!settings.Locked;settings.Save();
            ((TextBlock)lockButton.Content).Text=settings.Locked?"\uE72E":"\uE785";
            lockButton.ToolTip=settings.Locked?"解锁拖动":"锁定位置";
            pet.Cursor=settings.Locked?Cursors.Hand:Cursors.SizeAll;
        });
        DockPanel.SetDock(lockButton,Dock.Right);position.Children.Add(lockButton);
        var reset=Icon("\uE777","贴紧右下角",(_,_)=>{settings.OffsetX=0;settings.OffsetY=0;settings.AnchorVersion=1;settings.Save();Follow();});DockPanel.SetDock(reset,Dock.Right);position.Children.Add(reset);
        position.Children.Add(Text("位置",14));body.Children.Add(position);
        var sizeLine=new DockPanel{Margin=new Thickness(0,12,0,0)};
        var sizeLabel=Text($"{settings.Size:0} px",12,"MutedBrush");DockPanel.SetDock(sizeLabel,Dock.Right);sizeLine.Children.Add(sizeLabel);sizeLine.Children.Add(Text("尺寸"));body.Children.Add(sizeLine);
        var size=new Slider{Minimum=128,Maximum=360,Value=settings.Size,TickFrequency=8,IsSnapToTickEnabled=true,Margin=new Thickness(0,6,0,6)};
        size.ValueChanged+=(_,_)=>{settings.Size=size.Value;sizeLabel.Text=$"{size.Value:0} px";Follow();};
        size.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>settings.Save()));
        size.PreviewMouseLeftButtonUp+=(_,_)=>settings.Save();size.KeyUp+=(_,_)=>settings.Save();body.Children.Add(size);
        body.Children.Add(Text("外观",14));
        var modes=new UniformGrid{Columns=3,Margin=new Thickness(0,8,0,8)};
        var modeButtons=new List<(Button Button,string Mode)>();
        foreach(var (label,mode) in new[]{("跟随系统","system"),("浅色","light"),("深色","dark")})
        {
            var button=new Button{Content=Text(label,12),Margin=new Thickness(0,0,4,0),Padding=new Thickness(6)};
            button.SetResourceReference(Button.BorderBrushProperty,settings.Theme==mode?"AccentBrush":"BorderBrush");
            modeButtons.Add((button,mode));button.Click+=(_,_)=>
            {
                settings.Theme=mode;theme.Apply(mode);settings.Save();
                foreach(var b in modeButtons)b.Button.SetResourceReference(Button.BorderBrushProperty,b.Mode==mode?"AccentBrush":"BorderBrush");
            };modes.Children.Add(button);
        }
        body.Children.Add(modes);
        var reduced=new CheckBox{Content=Text("即时眼神跟随",12),IsChecked=settings.ReducedMotion,Margin=new Thickness(0,4,0,4)};
        reduced.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        reduced.Checked+=(_,_)=>{settings.ReducedMotion=true;settings.Save();};reduced.Unchecked+=(_,_)=>{settings.ReducedMotion=false;settings.Save();};body.Children.Add(reduced);
        body.Children.Add(Divider());
        body.Children.Add(Text("Claude in Codex · 0.2.2",11,"MutedBrush"));
        var border=new Border{Width=306,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Child=body,Padding=new Thickness(0)};
        border.SetResourceReference(Border.BackgroundProperty,"PanelBrush");border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        return border;
    }
    private async Task Refresh()
    {
        refresh.IsEnabled=false;status.Text="正在读取额度…";
        try{await usage.RefreshAsync();}finally{if(!closed)refresh.IsEnabled=true;}
    }
    private void UpdateUsage(UsageSnapshot snapshot)
    {
        if(closed)return;
        status.Text=snapshot.Status+(snapshot.Stale&&snapshot.Windows.Count>0?" · 上次数据":"");
        quotas.Children.Clear();
        if(snapshot.Windows.Count==0)quotas.Children.Add(Text("额度暂不可用",16,"MutedBrush"));
        foreach(var window in snapshot.Windows)
        {
            var row=new DockPanel{Margin=new Thickness(0,8,0,4)};
            var value=Text($"{window.Remaining:0.#}%",23,snapshot.Stale?"MutedBrush":"AccentBrush");DockPanel.SetDock(value,Dock.Right);row.Children.Add(value);row.Children.Add(Text(window.Label,13));quotas.Children.Add(row);
            var track=new Grid{Height=5,Margin=new Thickness(0,4,0,6)};
            track.SetResourceReference(BackgroundProperty,"SubtleBrush");
            var fill=new Border{HorizontalAlignment=HorizontalAlignment.Left,Width=272*window.Remaining/100,CornerRadius=new CornerRadius(2)};fill.SetResourceReference(Border.BackgroundProperty,snapshot.Stale?"MutedBrush":"AccentBrush");track.Children.Add(fill);quotas.Children.Add(track);
            var reset=window.ResetsAt is {} time?time.ToLocalTime().ToString("MM-dd HH:mm")+" 重置":"未提供重置时间";
            quotas.Children.Add(Text(reset,11,"MutedBrush"));
        }
        updated.Text=snapshot.UpdatedAt is {} when?$"更新于 {when.ToLocalTime():HH:mm:ss}":"尚无成功读取记录";
    }
    private void Cleanup()
    {
        closed=true;tracker.Stop();gazeTimer.Stop();refreshTimer.Stop();panel.IsOpen=false;
        usage.Dispose();theme.Dispose();settings.Save();if(!diagnostic)Application.Current.Shutdown();
    }
    internal async Task DiagnosticPanel(UsageSnapshot snapshot,string mode)
    {
        theme.Apply(mode);UpdateUsage(snapshot);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var border=(FrameworkElement)panel.Child;
        border.Measure(new Size(306,900));var h=(int)Math.Ceiling(border.DesiredSize.Height);
        Diagnostics.Save(Diagnostics.Render(border,306,h),$"panel-{mode}.png");
    }
    internal void DisposeForDiagnostics(){usage.Dispose();theme.Dispose();}
    internal bool DiagnosticClick()
    {
        TogglePanel();return panel.IsOpen;
    }
    internal bool DiagnosticLock()
    {
        lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return settings.Locked;
    }
    internal bool DiagnosticClosed=>closed;
    internal FrameworkElement DiagnosticLauncher=>root;
    internal bool DiagnosticLauncherVisible=>launcher.Visibility==Visibility.Visible&&pet.Visibility==Visibility.Collapsed;
    internal void DiagnosticClosePet()=>closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
