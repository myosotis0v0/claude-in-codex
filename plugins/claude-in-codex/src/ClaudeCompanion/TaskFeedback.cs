namespace ClaudeCompanion;

// Active turns sustain thinking; only a fresh, matched Stop triggers the short completion.
internal sealed class TaskFeedback
{
    internal const double CompleteSeconds=2;
    private long starts,completions,lastCueTicks;
    private DateTimeOffset thinkingBegan,began,until;
    private bool thinking;
    private string lastCue="";
    internal long Revision {get;private set;}
    internal long CompleteRevision {get;private set;}
    internal bool Thinking=>thinking;
    internal bool Observe(TaskActivity activity,DateTimeOffset now)
    {
        var changed=thinking!=(activity.Count>0);
        thinking=activity.Count>0;
        if(thinking&&changed)thinkingBegan=now;
        var start=activity.Starts>starts;var complete=activity.Completions>completions;
        starts=Math.Max(starts,activity.Starts);completions=Math.Max(completions,activity.Completions);
        var finish=complete&&(!start||activity.CompletedTicks>=activity.StartedTicks);
        if(finish||start)
        {
            var ticks=finish?activity.CompletedTicks:activity.StartedTicks;
            // Current activity survives pauses; historical completion cues are never replayed.
            if((ticks>lastCueTicks||ticks==lastCueTicks&&lastCue!="complete"&&finish)&&
                ticks>0&&ticks<=now.AddSeconds(1).UtcTicks&&now.UtcTicks-ticks<=TimeSpan.FromSeconds(30).Ticks)
            {
                lastCue=finish?"complete":"thinking";lastCueTicks=ticks;
                if(finish){CompleteRevision++;began=now;until=now.AddSeconds(CompleteSeconds);}
                else{thinkingBegan=now;until=default;}
                changed=true;
            }
        }
        if(changed)Revision++;
        return changed;
    }
    internal string StateAt(DateTimeOffset now)=>now>=began&&now<until?"complete":thinking&&now>=thinkingBegan?"thinking":"";
    internal double MotionTimeAt(DateTimeOffset now)=>StateAt(now)=="complete"
        ?Math.Clamp((now-began).TotalSeconds/CompleteSeconds,0,1)*MotionLab.Motions.Duration
        :Math.Max(0,(now-thinkingBegan).TotalSeconds)%MotionLab.Motions.Duration;
}
