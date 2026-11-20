using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ClaudeCompanion;

public sealed partial class CompanionWindow
{
    private readonly DispatcherTimer resetCreditTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private DateTimeOffset lastUsageAttempt;
    private UsageSnapshot? displayedUsage;
    private Expander creditSection=null!;
    private StackPanel creditRows=null!;
    private TextBlock creditSummary=null!;
    private Slider reminderDays=null!;
    private CheckBox reminderSwitch=null!;

    private Expander BuildResetCreditSection()
    {
        creditRows=new StackPanel{Margin=new Thickness(0,4,0,0)};
        creditSummary=Text("重置券");creditSummary.TextWrapping=TextWrapping.Wrap;
        creditSection=new Expander{Header=creditSummary,Content=creditRows,Margin=new Thickness(0,10,0,0),Visibility=Visibility.Collapsed};
        AutomationProperties.SetName(creditSection,"重置券明细");
        return creditSection;
    }

    private void BuildResetCreditSettings(StackPanel details)
    {
        reminderSwitch=new CheckBox{Content=Text("重置券临期提醒",12),IsChecked=settings.ResetCreditReminder};
        reminderSwitch.SetResourceReference(StyleProperty,"MotionSwitch");AutomationProperties.SetName(reminderSwitch,"重置券临期提醒");
        details.Children.Add(reminderSwitch);
        var row=new DockPanel{Margin=new Thickness(0,4,0,0)};
        var days=Text($"{settings.ResetCreditReminderDays} 天",12,"MutedBrush");
        DockPanel.SetDock(days,Dock.Right);row.Children.Add(days);row.Children.Add(Text("提前提醒",12));details.Children.Add(row);
        reminderDays=new Slider{Minimum=1,Maximum=ResetCredits.MaximumReminderDays,Value=settings.ResetCreditReminderDays,
            TickFrequency=1,SmallChange=1,LargeChange=7,IsSnapToTickEnabled=true,IsEnabled=settings.ResetCreditReminder,Margin=new Thickness(-3,4,-3,0)};
        AutomationProperties.SetName(reminderDays,"重置券提前提醒天数");
        reminderDays.ValueChanged+=(_,_)=>{
            settings.ResetCreditReminderDays=(int)reminderDays.Value;days.Text=$"{settings.ResetCreditReminderDays} 天";
            if(displayedUsage!=null)UpdateResetCredits(displayedUsage);
        };
        reminderDays.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>settings.Save()));
        reminderDays.PreviewMouseLeftButtonUp+=(_,_)=>settings.Save();reminderDays.KeyUp+=(_,_)=>settings.Save();
        details.Children.Add(reminderDays);
        void SetReminder(bool on)
        {
            settings.ResetCreditReminder=on;reminderDays.IsEnabled=on;settings.Save();
            if(displayedUsage!=null)UpdateResetCredits(displayedUsage);
            if(on&&!diagnostic&&IsVisible&&lastUsageAttempt<DateTimeOffset.UtcNow.AddMinutes(-2))_=Refresh();
        }
        reminderSwitch.Checked+=(_,_)=>SetReminder(true);reminderSwitch.Unchecked+=(_,_)=>SetReminder(false);
    }

    private void CheckResetReminder()
    {
        UpdateResetCredits(usage.Current);
        if(!panel.IsOpen&&settings.ResetCreditReminder&&lastUsageAttempt<DateTimeOffset.UtcNow.AddMinutes(-30))_=Refresh();
    }

    private void UpdateResetCredits(UsageSnapshot snapshot,DateTimeOffset? moment=null)
    {
        displayedUsage=snapshot;
        var now=moment??DateTimeOffset.UtcNow;
        var days=settings.ResetCreditReminderDays;
        var data=snapshot.ResetCredits;
        var soon=data?.ExpiringCount(now,days)??0;
        pet.SetResetRibbon(settings.ResetCreditReminder&&soon>0&&(!snapshot.Stale||pet.ResetRibbon));

        creditSection.Visibility=snapshot.UpdatedAt!=null?Visibility.Visible:Visibility.Collapsed;
        creditRows.Children.Clear();
        if(data==null)
        {
            creditSummary.Text="重置券 · 暂不可用";creditSummary.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
            creditSection.IsExpanded=false;creditSection.IsEnabled=false;return;
        }
        var suffix=soon>0?$" · {soon} 张临期":"";
        creditSummary.Text=$"重置券 · {data.AvailableCount} 张{suffix}";
        creditSummary.SetResourceReference(TextBlock.ForegroundProperty,soon>0&&!snapshot.Stale?"AccentBrush":"TextBrush");
        creditSection.IsEnabled=data.AvailableCount>0;
        if(!creditSection.IsEnabled){creditSection.IsExpanded=false;return;}
        foreach(var credit in data.Details??[])
        {
            var expires=credit.ExpiresAt;
            var text=expires is {} time?$"{time.ToLocalTime():MM-dd HH:mm} 到期":"未提供到期时间";
            if(expires is {} until&&until<=now)text+=" · 已过期";
            else if(credit.IsExpiring(now,days))text+=" · 临期";
            AddRow(text,credit.IsExpiring(now,days)&&!snapshot.Stale?"AccentBrush":"MutedBrush");
        }
        int missing=data.AvailableCount-(data.Details?.Count??0);
        if(missing>0)AddRow($"{missing} 张未返回到期明细","MutedBrush");
        void AddRow(string text,string color)
        {
            var row=Text(text,12,color);row.TextWrapping=TextWrapping.Wrap;row.Margin=new Thickness(0,2,0,2);creditRows.Children.Add(row);
        }
    }

    internal Expander DiagnosticCreditSection=>creditSection;
    internal TextBlock DiagnosticCreditSummary=>creditSummary;
    internal Slider DiagnosticReminderDays=>reminderDays;
    internal CheckBox DiagnosticReminderSwitch=>reminderSwitch;
    internal bool DiagnosticRibbon=>pet.ResetRibbon;
    internal void DiagnosticResetCredits(UsageSnapshot snapshot,DateTimeOffset now)=>UpdateResetCredits(snapshot,now);
}
