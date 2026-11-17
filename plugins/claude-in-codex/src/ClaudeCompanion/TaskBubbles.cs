namespace ClaudeCompanion;

internal sealed record TaskBubbleFrame(string Session,string State,int Slot,double Opacity,double Progress,string Title="",int Capacity=0);

internal sealed class TaskBubbles
{
    private sealed class Bubble(int slot,DateTimeOffset began)
    {
        internal readonly int Slot=slot;
        internal readonly DateTimeOffset Began=began;
        internal bool Active;
        internal DateTimeOffset CompletedAt,Until;
    }
    private readonly Dictionary<string,Bubble> bubbles=[];
    private readonly Dictionary<string,long> observedCompletions=[];
    private int capacity;
    internal void Observe(TaskActivity activity,DateTimeOffset now)
    {
        if(activity.Sessions is not {} sessions)return;
        foreach(var key in bubbles.Where(p=>!p.Value.Active&&now>=p.Value.Until).Select(p=>p.Key).ToArray())bubbles.Remove(key);
        if(bubbles.Count==0)capacity=0;
        foreach(var bubble in bubbles.Values)bubble.Active=false;
        var current=new HashSet<string>();
        foreach(var session in sessions)
        {
            current.Add(session.Session);
            var fresh=session.CompletedTicks>observedCompletions.GetValueOrDefault(session.Session);
            observedCompletions[session.Session]=Math.Max(session.CompletedTicks,observedCompletions.GetValueOrDefault(session.Session));
            fresh&=session.CompletedTicks>0&&session.CompletedTicks<=now.AddSeconds(1).UtcTicks&&now.UtcTicks-session.CompletedTicks<=TimeSpan.FromSeconds(30).Ticks;
            if(!session.Active&&!fresh&&!bubbles.ContainsKey(session.Session))continue;
            if(!bubbles.TryGetValue(session.Session,out var bubble))
            {
                var used=bubbles.Values.Select(b=>b.Slot).ToHashSet();var slot=0;while(used.Contains(slot))slot++;
                bubbles[session.Session]=bubble=new(slot,now);
                capacity=Math.Max(capacity,slot+1);
            }
            bubble.Active=session.Active;
            if(fresh){bubble.CompletedAt=now;bubble.Until=now.AddSeconds(TaskFeedback.CompleteSeconds);}
        }
        foreach(var key in observedCompletions.Keys.Where(k=>!current.Contains(k)).ToArray())observedCompletions.Remove(key);
        foreach(var key in bubbles.Where(p=>!p.Value.Active&&now>=p.Value.Until).Select(p=>p.Key).ToArray())bubbles.Remove(key);
    }
    internal IReadOnlyList<TaskBubbleFrame> Frames(DateTimeOffset now)=>bubbles.OrderBy(p=>p.Value.Slot).Select(p=>
    {
        var b=p.Value;var complete=now>=b.CompletedAt&&now<b.Until;
        var opacity=Math.Clamp((now-b.Began).TotalSeconds/.18,0,1);
        if(complete)opacity*=Math.Clamp((b.Until-now).TotalSeconds/.35,0,1);
        return new TaskBubbleFrame(p.Key,complete?"complete":"thinking",b.Slot,
            complete||b.Active?opacity:0,complete?Math.Clamp((now-b.CompletedAt).TotalSeconds/TaskFeedback.CompleteSeconds,0,1):0,Capacity:capacity);
    }).Where(b=>b.Opacity>0).ToArray();
}
