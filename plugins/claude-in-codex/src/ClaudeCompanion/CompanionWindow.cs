using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Data;

namespace ClaudeCompanion;

public sealed class CompanionWindow : Window
{
    private readonly Settings settings;
    private readonly Func<HashSet<int>> discover;
    private readonly Func<HashSet<int>,nint> findTarget;
    private readonly bool diagnostic;
    private readonly Func<Task> readUsage;
    private readonly PluginLifetime pluginLifetime=new();
    private readonly Grid root=new();
    private Button launcher=null!;
    private bool expanded;
    private readonly Settings launcherPosition=new(){Size=36,OffsetX=0,OffsetY=0,PersistenceEnabled=false};
    private readonly ThemeManager theme=new();
    private readonly UsageClient usage=new();
    private readonly PetView pet=new();
    private readonly DeskKeyboard deskKeyboard=new();
    private bool DeskActive=>!diagnostic&&expanded&&settings.DeskInput&&IsVisible&&target!=0&&Native.GetForegroundWindow()==target;
    private readonly TaskEvents taskEvents=new();
    private readonly Stopwatch animationClock=Stopwatch.StartNew();
    private double lastFrame;
    private DateTime lastTaskPoll=DateTime.MinValue;
    private bool pollingTasks,pressing,patted;
    private DateTime pressedAt;
    private Point pressedPoint;
    private Native.Point previousCursor;
    private bool cursorSeen;
    private TaskActivity activity=new(0,false,false);
    private TextBlock taskStatus=new();
    private int renderPadX,renderPadY;
    private readonly DispatcherTimer tracker=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DispatcherTimer gazeTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromMinutes(2)};
    private readonly Popup panel=new(){AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Custom,PopupAnimation=PopupAnimation.Fade};
    private StackPanel quotas=new();
    private TextBlock status=new(),updated=new();
    private Button lockButton=null!,closeButton=null!,resetButton=null!;
    private Border panelChrome=null!;
    private ScrollViewer panelScroll=null!;
    private Expander otherSettings=null!;
    private Slider sizeSlider=null!;
    private CheckBox motionSwitch=null!;
    private readonly Dictionary<string,RadioButton> themeChoices=[];
    private Size panelHost=new(1280,800);
    private Rect panelPet=new(1000,556,224,224);
    private PanelConstraint panelConstraint;
    private bool refreshing;
    private nint handle,target;
    private HashSet<int> pids=[];
    private DateTime lastDiscovery=DateTime.MinValue;
    private bool seenTarget,dragging,moved,closed;
    private Native.Point pointerStart;
    private int windowStartX,windowStartY,currentX,currentY,currentSize;
    private double targetScale=1;
    public CompanionWindow(Func<HashSet<int>>? discover=null,Func<HashSet<int>,nint>? findTarget=null,bool diagnostic=false,Func<Task>? readUsage=null)
    {
        this.discover=discover??Native.CodexPids;this.findTarget=findTarget??Native.FindWindow;this.diagnostic=diagnostic;
        this.readUsage=readUsage??(diagnostic?()=>Task.CompletedTask:usage.RefreshAsync);
        settings=diagnostic?new Settings{PersistenceEnabled=false}:Settings.Load();
        Title="Claude in Codex";Width=settings.Size;Height=settings.Size;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=false;
        root.Children.Add(pet);pet.Visibility=Visibility.Collapsed;
        Content=root;Opacity=0;
        pet.Cursor=settings.Locked?Cursors.Hand:Cursors.SizeAll;
        pet.ToolTip=null;ToolTipService.SetIsEnabled(pet,false);
        if(!diagnostic)pet.EnableMotion();
        if(pet.Hands is {} hands)hands.Enabled=settings.DeskInput;
        if(!diagnostic)Settings.Log("Startup stage=artwork-ready");
        theme.Apply(settings.Theme);
        panel.PlacementTarget=this;panel.Child=BuildPanel();BuildLauncher();
        UpdatePanelGeometry(panelHost,panelPet);
        panel.CustomPopupPlacementCallback=(popupSize,_,_)=>
        {
            var point=PanelGeometry.Place(popupSize,panelHost,panelPet,panelConstraint.Above);
            return [new CustomPopupPlacement(new Point(point.X+renderPadX/targetScale,point.Y+renderPadY/targetScale),PopupPrimaryAxis.None)];
        };
        theme.Applied+=RefreshTheme;RefreshTheme();
        panel.Opened+=async(_,_)=>{if(!diagnostic){refreshTimer.Start();if(!greetingPopup)LookAtPanel();}greetingPopup=false;await PreparePanelOpen();};
        panel.Closed+=(_,_)=>{greetingPopup=false;pet.Controller?.CancelQueuedMenu();refreshTimer.Stop();};
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
        pet.LostMouseCapture+=(_,_)=>{pressing=false;if(dragging){dragging=false;SavePosition();}};
        tracker.Tick+=(_,_)=>Follow();
        gazeTimer.Tick+=(_,_)=>Look();
        refreshTimer.Tick+=async(_,_)=>{if(panel.IsOpen)await Refresh();};
        usage.Changed+=snapshot=>Dispatcher.BeginInvoke(()=>UpdateUsage(snapshot));
        usage.ThreadTitlesChanged+=titles=>Dispatcher.BeginInvoke(()=>pet.SetThreadTitles(titles));
        Closed+=(_,_)=>Cleanup();
    }
    private nint WndProc(nint hwnd,int msg,nint w,nint l,ref bool handled)
    {
        if(msg==0xff&&DeskActive&&pet.Hands is {} hands)deskKeyboard.Receive(l,hands);
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
            deskKeyboard.Enable(handle,false);cursorSeen=false;pet.Hands?.Reset();
            panel.IsOpen=false;gazeTimer.Stop();refreshTimer.Stop();lastFrame=0;pressing=false;
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
                PlaceRenderWindow(origin.X,origin.Y);
                if(panel.IsOpen){panel.HorizontalOffset=0.01;panel.HorizontalOffset=0;}
            }
        }
        UpdatePanelGeometry(new Size(client.Width/targetScale,client.Height/targetScale),
            new Rect((currentX-origin.X)/targetScale,(currentY-origin.Y)/targetScale,currentSize/targetScale,currentSize/targetScale));
        if(!IsVisible)Show();
        PollTasks();
        if(Opacity<1&&!diagnostic)Settings.Log($"Startup stage=launcher-visible dpi={targetScale:0.##} bounds={currentX},{currentY},{currentSize}");
        Opacity=1;
        if(expanded&&!gazeTimer.IsEnabled)gazeTimer.Start();
        if(!expanded){gazeTimer.Stop();lastFrame=0;}
    }
    private void PlaceRenderWindow(int hostX,int hostY)
    {
        var padding=expanded&&pet.HasMotion?(int)Math.Ceiling(currentSize*73d/1254):0;
        var topPadding=expanded&&pet.HasMotion?Math.Max(padding,(int)Math.Ceiling(100*targetScale)):padding;
        renderPadX=Math.Min(padding,Math.Max(0,currentX-hostX));renderPadY=Math.Min(topPadding,Math.Max(0,currentY-hostY));
        pet.ArtworkSize=currentSize/targetScale;pet.RenderInset=new Vector(renderPadX/targetScale,renderPadY/targetScale);
        Native.SetWindowPos(handle,0,currentX-renderPadX,currentY-renderPadY,currentSize+renderPadX,currentSize+renderPadY,0x14);
        pet.InvalidateVisual();
    }
    private async void PollTasks()
    {
        if(diagnostic||pollingTasks||(DateTime.UtcNow-lastTaskPoll).TotalSeconds<1)return;
        pollingTasks=true;lastTaskPoll=DateTime.UtcNow;
        try
        {
            var next=await Task.Run(()=>taskEvents.Drain());if(closed)return;
            pet.Controller?.ObserveTasks(next);
            if(next.Count>0||next.Completions>activity.Completions)
                _=usage.RefreshThreadTitlesAsync((next.Sessions??[]).Where(s=>s.Active||s.CompletedTicks>DateTimeOffset.UtcNow.AddSeconds(-30).UtcTicks).Select(s=>s.Session).ToHashSet());
            activity=next;UpdateTaskStatus();
        }
        finally{pollingTasks=false;}
    }
    private void UpdateTaskStatus()=>taskStatus.Text=activity.Count>0?$"已收到开始事件 · {activity.Count} 个会话未收到结束":
        activity.Expired?"任务事件已过期":activity.Seen?"已收到结束事件":"尚未收到任务事件";
    private void Look()
    {
        var active=DeskActive;deskKeyboard.Enable(handle,active);
        if(!active)pet.Hands?.Reset();
        if(Native.GetCursorPos(out var cursor))
        {
            if(active&&cursorSeen)pet.Hands?.Cursor((float)((cursor.X-previousCursor.X)/targetScale));
            if(cursorSeen&&Math.Abs(cursor.X-previousCursor.X)+Math.Abs(cursor.Y-previousCursor.Y)>3)pet.Controller?.Touch();
            previousCursor=cursor;cursorSeen=true;
            var centerX=currentX+currentSize*0.5;var centerY=currentY+currentSize*0.65;
            var range=Math.Max(240,360*targetScale);
            pet.Look(new Vector((cursor.X-centerX)/range,(cursor.Y-centerY)/range),settings.ReducedMotion);
        }
        var now=animationClock.Elapsed.TotalSeconds;var delta=lastFrame==0?1/30d:now-lastFrame;lastFrame=now;
        pet.Animate(delta,settings.AmbientWind);
        if(pressing&&!patted&&settings.Locked&&pet.IsMouseCaptured&&
            (Mouse.GetPosition(pet)-pressedPoint).Length<6&&(DateTime.UtcNow-pressedAt).TotalMilliseconds>=480)
        {patted=true;pet.Controller?.React("pat");}
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
    private bool greetingPopup;
    public void Expand()
    {
        lastFrame=0;pet.Controller?.Touch();pet.Controller?.React("wink");
        expanded=true;pet.Visibility=Visibility.Visible;launcher.Visibility=Visibility.Collapsed;
        if(!diagnostic){Follow();if(IsVisible&&!panel.IsOpen){greetingPopup=true;TogglePanel();}}
    }
    private void Collapse()
    {
        deskKeyboard.Enable(handle,false);pet.Hands?.Reset();cursorSeen=false;
        panel.IsOpen=false;dragging=false;pressing=false;pet.ReleaseMouseCapture();expanded=false;
        pet.Visibility=Visibility.Collapsed;launcher.Visibility=Visibility.Visible;
        if(!diagnostic)Follow();
    }
    private void Pressed(object sender,MouseButtonEventArgs e)
    {
        moved=false;patted=false;pet.Controller?.Touch();pressedAt=DateTime.UtcNow;pressedPoint=e.GetPosition(pet);
        pressing=settings.Locked&&pet.HeadHit(pressedPoint);
        if(pressing)pet.CaptureMouse();
        if(!settings.Locked && Native.GetCursorPos(out pointerStart))
        {
            dragging=true;windowStartX=currentX;windowStartY=currentY;pet.CaptureMouse();panel.IsOpen=false;
        }
        e.Handled=true;
    }
    private void Dragged(object sender,MouseEventArgs e)
    {
        if(pressing&&(e.GetPosition(pet)-pressedPoint).Length>=6){pressing=false;moved=true;}
        if(!dragging||!Native.GetCursorPos(out var point))return;
        var dx=point.X-pointerStart.X;var dy=point.Y-pointerStart.Y;
        if(!moved&&Math.Abs(dx)+Math.Abs(dy)<5*targetScale)return;
        moved=true;
        if(!Native.GetClientRect(target,out var client))return;
        var origin=new Native.Point();Native.ClientToScreen(target,ref origin);
        currentX=Math.Clamp(windowStartX+dx,origin.X,origin.X+Math.Max(0,client.Width-currentSize));
        currentY=Math.Clamp(windowStartY+dy,origin.Y,origin.Y+Math.Max(0,client.Height-currentSize));
        PlaceRenderWindow(origin.X,origin.Y);
    }
    private void Released(object sender,MouseButtonEventArgs e)
    {
        var wasDragging=dragging;dragging=false;pressing=false;pet.ReleaseMouseCapture();
        if(wasDragging&&moved)SavePosition();
        if(!moved&&!patted)TogglePanel();
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
    }
    private async Task PreparePanelOpen()
    {
        otherSettings.IsExpanded=false;panelScroll.ScrollToTop();UpdateUsage(usage.Current);
        await Refresh();
    }
    private void LookAtPanel()
    {
        panelChrome.UpdateLayout();var size=panelChrome.RenderSize;
        if(size.Width<=0||size.Height<=0)size=new(panelConstraint.Width,Math.Min(240,panelConstraint.MaxHeight));
        var origin=PanelGeometry.Place(size,panelHost,panelPet,panelConstraint.Above);
        var direction=new Vector(origin.X+size.Width*.5-panelPet.Width*.5,
            origin.Y+Math.Min(size.Height*.45,120)-panelPet.Height*.65);
        pet.Controller?.LookAtMenu(direction/Math.Max(240,panelPet.Width));
    }
    private void UpdatePanelGeometry(Size host,Rect anchor)
    {
        panelHost=host;panelPet=anchor;panelConstraint=PanelGeometry.Constrain(host,anchor);
        panelChrome.Width=panelConstraint.Width;panelChrome.MaxHeight=panelConstraint.MaxHeight;
    }
    private void ApplyTheme(string mode)
    {
        settings.Theme=mode;theme.Apply(mode);settings.Save();pet.Controller?.React("nod");
    }
    private void UpdateThemeSelection()
    {
        foreach(var (mode,button) in themeChoices)button.IsChecked=settings.Theme==mode;
    }
    private void RefreshTheme()
    {
        ThemeManager.ApplyScope(root);ThemeManager.ApplyScope(panelChrome);UpdateThemeSelection();
    }
    private static TextBlock Text(string value,int size=13,string? resource=null)
    {
        var text=new TextBlock{Text=value,FontSize=size};
        if(resource!=null)text.SetResourceReference(TextBlock.ForegroundProperty,resource);
        return text;
    }
    private new static Button Icon(string glyph,string tooltip,RoutedEventHandler action)
    {
        var button=new Button{Content=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15},ToolTip=tooltip,Margin=new Thickness(4,0,0,0)};
        button.SetResourceReference(StyleProperty,"IconButton");AutomationProperties.SetName(button,tooltip);
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
        var body=new StackPanel{Margin=new Thickness(18,16,18,14)};
        var header=new DockPanel();
        var commands=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(commands,Dock.Right);
        closeButton=Icon("\uE711","关闭桌宠，保留右下角入口",(_,_)=>Collapse());
        commands.Children.Add(closeButton);header.Children.Add(commands);
        var title=Text("Claude",20,"AccentBrush");title.VerticalAlignment=VerticalAlignment.Center;title.FontWeight=FontWeights.SemiBold;
        header.Children.Add(title);body.Children.Add(header);
        status.FontSize=12;status.Margin=new Thickness(0,4,0,12);status.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");body.Children.Add(status);
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
        resetButton=Icon("\uE777","重置到右下角",(_,_)=>{settings.OffsetX=0;settings.OffsetY=0;settings.AnchorVersion=1;settings.Save();if(!diagnostic)Follow();});
        DockPanel.SetDock(resetButton,Dock.Right);position.Children.Add(resetButton);
        var positionLabel=Text("位置",13);positionLabel.VerticalAlignment=VerticalAlignment.Center;position.Children.Add(positionLabel);body.Children.Add(position);

        var details=new StackPanel();
        var sizeLine=new DockPanel();
        var sizeLabel=Text($"{settings.Size:0}",12,"MutedBrush");sizeLabel.MinWidth=36;sizeLabel.TextAlignment=TextAlignment.Right;
        DockPanel.SetDock(sizeLabel,Dock.Right);sizeLine.Children.Add(sizeLabel);sizeLine.Children.Add(Text("尺寸"));details.Children.Add(sizeLine);
        sizeSlider=new Slider{Minimum=128,Maximum=360,Value=settings.Size,TickFrequency=8,SmallChange=8,LargeChange=32,IsSnapToTickEnabled=true,Margin=new Thickness(-3,4,-3,14)};
        AutomationProperties.SetName(sizeSlider,"桌宠尺寸");
        sizeSlider.ValueChanged+=(_,_)=>{settings.Size=sizeSlider.Value;sizeLabel.Text=$"{sizeSlider.Value:0}";if(!diagnostic)Follow();};
        sizeSlider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>settings.Save()));
        sizeSlider.PreviewMouseLeftButtonUp+=(_,_)=>settings.Save();sizeSlider.KeyUp+=(_,_)=>settings.Save();details.Children.Add(sizeSlider);
        details.Children.Add(Text("外观"));
        var modes=new UniformGrid{Columns=3};var group=Guid.NewGuid().ToString("N");
        foreach(var (label,mode,glyph) in new[]{("系统","system","\uE770"),("浅色","light","\uE706"),("深色","dark","\uE708")})
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal};
            var icon=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=13,Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};
            var text=Text(label,12);content.Children.Add(icon);content.Children.Add(text);
            var button=new RadioButton{Content=content,GroupName=group,IsChecked=settings.Theme==mode,ToolTip=mode=="system"?"跟随系统":label};
            button.SetResourceReference(StyleProperty,"ThemeSegment");AutomationProperties.SetName(button,button.ToolTip.ToString());
            icon.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=button});text.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=button});
            themeChoices.Add(mode,button);button.Checked+=(_,_)=>{if(settings.Theme!=mode)ApplyTheme(mode);};modes.Children.Add(button);
        }
        var segmented=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(3),Margin=new Thickness(0,8,0,14),Child=modes};
        segmented.SetResourceReference(Border.BackgroundProperty,"SubtleBrush");details.Children.Add(segmented);
        motionSwitch=new CheckBox{Content=Text("即时眼神跟随",12),IsChecked=settings.ReducedMotion};
        motionSwitch.SetResourceReference(StyleProperty,"MotionSwitch");AutomationProperties.SetName(motionSwitch,"即时眼神跟随");
        motionSwitch.Checked+=(_,_)=>{settings.ReducedMotion=true;settings.Save();};motionSwitch.Unchecked+=(_,_)=>{settings.ReducedMotion=false;settings.Save();};details.Children.Add(motionSwitch);
        var windSwitch=new CheckBox{Content=Text("发丝微风",12),IsChecked=settings.AmbientWind};
        windSwitch.SetResourceReference(StyleProperty,"MotionSwitch");AutomationProperties.SetName(windSwitch,"发丝微风");
        windSwitch.Checked+=(_,_)=>{settings.AmbientWind=true;settings.Save();};
        windSwitch.Unchecked+=(_,_)=>{settings.AmbientWind=false;settings.Save();};details.Children.Add(windSwitch);
        var desk=new CheckBox{Content=Text("键鼠联动（仅 Codex 前台）",12),IsChecked=settings.DeskInput};
        desk.SetResourceReference(StyleProperty,"MotionSwitch");AutomationProperties.SetName(desk,"键鼠联动");
        void SetDesk(bool on){
            settings.DeskInput=on;settings.Save();
            if(pet.Hands is {} hands){hands.Reset();hands.Enabled=on;}
            deskKeyboard.Enable(handle,DeskActive);pet.InvalidateVisual();
        }
        desk.Checked+=(_,_)=>SetDesk(true);desk.Unchecked+=(_,_)=>SetDesk(false);details.Children.Add(desk);
        details.Children.Add(Divider());
        taskStatus=Text("",11,"MutedBrush");taskStatus.TextWrapping=TextWrapping.Wrap;UpdateTaskStatus();details.Children.Add(taskStatus);
        otherSettings=new Expander{Header=Text("其它设置",13),Content=details,IsExpanded=false,Margin=new Thickness(0,8,0,0)};
        body.Children.Add(otherSettings);
        body.Children.Add(Divider());
        body.Children.Add(Text($"Claude in Codex · {typeof(App).Assembly.GetName().Version?.ToString(3)}",11,"MutedBrush"));
        panelScroll=new ScrollViewer{Content=body};panelScroll.SetResourceReference(StyleProperty,"PanelScroller");
        panelChrome=new Border{Width=320,MaxHeight=PanelGeometry.MaximumHeight,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Child=panelScroll};
        panelChrome.SetResourceReference(Border.BackgroundProperty,"PanelBrush");panelChrome.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        return panelChrome;
    }
    private async Task Refresh()
    {
        if(refreshing||closed)return;
        refreshing=true;status.Text="正在读取额度…";
        try{await readUsage();}
        finally{refreshing=false;if(!closed)UpdateUsage(usage.Current);}
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
            var track=new Grid{Height=5,Margin=new Thickness(0,4,0,6),ClipToBounds=true};
            track.SetResourceReference(BackgroundProperty,"SubtleBrush");
            track.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(window.Remaining,GridUnitType.Star)});
            track.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100-window.Remaining,GridUnitType.Star)});
            var fill=new Border{CornerRadius=new CornerRadius(2)};fill.SetResourceReference(Border.BackgroundProperty,snapshot.Stale?"MutedBrush":"AccentFillBrush");track.Children.Add(fill);quotas.Children.Add(track);
            var reset=window.ResetsAt is {} time?time.ToLocalTime().ToString("MM-dd HH:mm")+" 重置":"未提供重置时间";
            quotas.Children.Add(Text(reset,11,"MutedBrush"));
        }
        updated.Text=snapshot.UpdatedAt is {} when?$"更新于 {when.ToLocalTime():HH:mm:ss}":"尚无成功读取记录";
    }
    private void Cleanup()
    {
        deskKeyboard.Enable(handle,false);
        closed=true;tracker.Stop();gazeTimer.Stop();refreshTimer.Stop();panel.IsOpen=false;
        usage.Dispose();theme.Dispose();pet.DisposeMotion();settings.Save();if(!diagnostic)Application.Current.Shutdown();
    }
    internal async Task DiagnosticPanel(UsageSnapshot snapshot,string mode,bool details=false,string? filename=null)
    {
        ApplyTheme(mode);otherSettings.IsExpanded=details;UpdateUsage(snapshot);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var border=(FrameworkElement)panel.Child;
        border.Measure(new Size(panelChrome.Width,panelChrome.MaxHeight));var h=(int)Math.Ceiling(border.DesiredSize.Height);
        Diagnostics.Save(Diagnostics.Render(border,(int)panelChrome.Width,h),filename??$"panel-{mode}{(details?"-expanded":"")}.png");
    }
    internal Border DiagnosticPanelRoot=>panelChrome;
    internal ScrollViewer DiagnosticScroll=>panelScroll;
    internal Expander DiagnosticOther=>otherSettings;
    internal TextBlock DiagnosticTaskStatus=>taskStatus;
    internal Slider DiagnosticSize=>sizeSlider;
    internal CheckBox DiagnosticMotion=>motionSwitch;
    internal Settings DiagnosticSettings=>settings;
    internal Task DiagnosticOpen()=>PreparePanelOpen();
    internal void DiagnosticTheme(string mode)=>ApplyTheme(mode);
    internal void DiagnosticPanelAnchor(Size host,Rect anchor)=>UpdatePanelGeometry(host,anchor);
    internal Point DiagnosticPanelPoint(Size size)=>PanelGeometry.Place(size,panelHost,panelPet,panelConstraint.Above);
    internal void DisposeForDiagnostics(){usage.Dispose();theme.Dispose();pet.DisposeMotion();}
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
