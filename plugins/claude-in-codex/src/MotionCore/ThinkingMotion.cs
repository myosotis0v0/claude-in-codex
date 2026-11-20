namespace MotionLab;

internal sealed class ThinkingMotion(Random random)
{
    internal const double DriftMin=15,DriftMax=20,DriftDuration=1.8;
    private const double RestDuration=1.90;
    private enum Beat { None, Review, Rest, Recover, Drift }
    private Beat beat;
    private bool continuous,restNext;
    private double start,nextBeat,nextDrift;
    private float recovery;

    internal Pose Step(Pose pose,double clock,bool active,bool visible,bool handsFree)
    {
        if(!active){continuous=false;beat=Beat.None;return pose;}
        if(!continuous)
        {
            continuous=true;restNext=false;nextBeat=clock+.24;nextDrift=clock+DriftGap();
        }
        if(!visible)
        {
            if(clock>=nextDrift)nextDrift=clock+DriftGap();
            beat=Beat.None;nextBeat=clock+.30;return pose;
        }
        var elapsed=clock-start;
        if(beat==Beat.Rest&&!handsFree)
        {
            recovery=RestHead(elapsed);beat=Beat.Recover;start=clock;elapsed=0;
        }
        var duration=beat switch{Beat.Review=>1.80,Beat.Rest=>RestDuration,Beat.Recover=>.20,Beat.Drift=>DriftDuration,_=>0};
        if(elapsed>=duration)beat=Beat.None;
        if(beat==Beat.None&&clock>=nextDrift)
        {
            beat=Beat.Drift;start=clock;elapsed=0;
            nextDrift=clock+DriftGap();nextBeat=clock+DriftDuration+.45;
        }
        else if(beat==Beat.None&&clock>=nextBeat)
        {
            // An unavailable rest is skipped rather than queued behind typing.
            beat=restNext&&handsFree?Beat.Rest:Beat.Review;
            restNext=!restNext;start=clock;elapsed=0;
            nextBeat=clock+2+2*random.NextDouble();
        }
        return beat switch
        {
            Beat.Review=>Review(pose,elapsed),
            Beat.Rest=>Rest(pose,RestHead(elapsed)) with{ThinkingRest=Acting.Beat(elapsed,0,.48,1.30,1.84)},
            Beat.Recover=>Rest(pose,recovery*(1-Acting.Beat(elapsed,0,.20,.21,.22))),
            Beat.Drift=>Acting.Apply("thinking",pose,4.65*elapsed/DriftDuration),
            _=>pose
        };
    }
    private double DriftGap()=>DriftMin+(DriftMax-DriftMin-RestDuration)*random.NextDouble();
    private static float RestHead(double t)=>Acting.Beat(t,.10,.55,1.26,1.74);
    private static Pose Rest(Pose p,float w)=>p with
    {
        HeadX=p.HeadX-41*w,HeadY=p.HeadY+47*w,HeadAngle=p.HeadAngle-4.8f*w
    };
    private static Pose Review(Pose p,double t)
    {
        var gaze=Acting.Beat(t,.03,.62,1.07,1.70);
        var face=Acting.Beat(t,.15,.45,1.19,1.59);
        var head=Acting.Beat(t,.35,.68,1.19,1.72);
        return p with
        {
            ThinkingReview=face,GazeX=p.GazeX+(-.88f-p.GazeX)*gaze,GazeY=p.GazeY+(.03f-p.GazeY)*gaze,
            BrowLeft=p.BrowLeft+3.6f*face,BrowRight=p.BrowRight-3.6f*face,
            HeadX=p.HeadX-7*head,HeadY=p.HeadY+4*head,HeadAngle=p.HeadAngle-1.3f*head
        };
    }
}
