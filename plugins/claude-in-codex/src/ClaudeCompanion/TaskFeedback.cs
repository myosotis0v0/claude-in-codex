namespace ClaudeCompanion;

// Observed edges trigger bounded reactions; aggregate task counts never hold an animation open.
internal sealed class TaskFeedback
{
    internal const double StartSeconds=3,CompleteSeconds=2;
    private long starts,completions,lastCueTicks;
    private DateTimeOffset began,until;
    private string state="";
    internal long Revision {get;private set;}
    internal bool Observe(TaskActivity activity,DateTimeOffset now)
    {
        var start=activity.Starts>starts;var complete=activity.Completions>completions;
        starts=Math.Max(starts,activity.Starts);completions=Math.Max(completions,activity.Completions);
        var finish=complete&&(!start||activity.CompletedTicks>=activity.StartedTicks);
        if(!finish&&!start)return false;
        var ticks=finish?activity.CompletedTicks:activity.StartedTicks;
        if(ticks<lastCueTicks||ticks==lastCueTicks&&(state=="complete"||!finish))return false;
        // Do not replay an old spool backlog after relaunch, or accept a future-dated cue.
        if(ticks<=0||ticks>now.AddSeconds(1).UtcTicks||now.UtcTicks-ticks>TimeSpan.FromSeconds(30).Ticks)return false;
        state=finish?"complete":"thinking";began=now;
        lastCueTicks=ticks;until=now.AddSeconds(finish?CompleteSeconds:StartSeconds);Revision++;return true;
    }
    internal string StateAt(DateTimeOffset now)=>now>=began&&now<until?state:"";
    internal double MotionTimeAt(DateTimeOffset now)=>until<=began?0:Math.Clamp((now-began).TotalSeconds/(until-began).TotalSeconds,0,1)*MotionLab.Motions.Duration;
}
