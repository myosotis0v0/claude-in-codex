namespace MotionLab;

internal sealed class DeskHands
{
    internal bool Enabled;
    private const float MouseLimitX=28,MouseStart=-MouseLimitX*.5f;
    internal float KeyX,KeyY,MouseX=MouseStart;
    internal bool Contact {get;private set;}
    private bool pendingStrike,liftFrame;
    private float target,released=1,dx=MouseStart,travelStart,travelGoal;
    private readonly System.Collections.Generic.List<(int Id,float X)> held=new();
    internal static float? Column(int sc,bool extended){
        if(extended)return sc switch{0x1c or 0x48 or 0x50=>.91f,0x4b=>.84f,0x4d=>1,_=>null};
        return sc switch{
            >=0x02 and <=0x0d=>(sc-2)/13f, >=0x10 and <=0x1b=>(sc-0x10+.5f)/13,
            >=0x1e and <=0x28=>(sc-0x1e+.8f)/13, >=0x2c and <=0x35=>(sc-0x2c+1.3f)/13,
            0x01 or 0x0f or 0x29 or 0x2a or 0x3a=>0,0x39=>.47f,
            0x0e or 0x1c or 0x2b or 0x36=>1,>=0x3b and <=0x44=>(sc-0x3b)/11f,
            0x57 or 0x58 or >=0x47 and <=0x53=>1,_=>null};
    }
    internal void Key(int scan,bool extended,bool up=false){
        if(!Enabled||Column(scan,extended) is not float col)return;
        int id=scan|(extended?256:0), index=held.FindIndex(k=>k.Id==id);
        if(up){
            if(index<0)return;
            bool current=index==held.Count-1;
            held.RemoveAt(index);
            if(held.Count==0)released=0;
            else if(current)Reach(held[^1].X);
        }else if(index<0){
            Reach(510+col*541-930);held.Add((id,target));
        }
    }
    private void Reach(float x){
        // Only the newest strike is pending. Show a lift between consecutive contacts.
        target=x;liftFrame=Contact;pendingStrike=true;
    }
    internal void Cursor(float x){dx=Math.Clamp(dx+x*.15f,-MouseLimitX,MouseLimitX);}
    internal void Step(float dt){
        var w=1-MathF.Exp(-dt*21);
        if(held.Count==0)released=Math.Min(1,released+dt);
        bool returning=held.Count==0&&released>=.5f;
        var goal=returning?0:target;
        if(goal!=travelGoal){travelStart=KeyX;travelGoal=goal;}
        KeyX+=(goal-KeyX)*(1-MathF.Exp(-dt*(returning?12:34)));
        var distance=goal-travelStart;
        var progress=distance==0?1:Math.Clamp((KeyX-travelStart)/distance,0,1);
        var arc=Math.Clamp((Math.Abs(distance)-48)*.08f,0,16);
        KeyY=-arc*4*progress*(1-progress);
        Contact=!returning&&!liftFrame&&(held.Count!=0||pendingStrike)&&Math.Abs(goal-KeyX)<20;
        if(Contact)pendingStrike=false;
        liftFrame=false;
        MouseX+=(dx-MouseX)*w;
    }
    internal void Reset(){held.Clear();target=KeyX=KeyY=travelStart=travelGoal=0;released=1;Contact=pendingStrike=liftFrame=false;}
}
