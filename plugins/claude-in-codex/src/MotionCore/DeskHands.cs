namespace MotionLab;

internal sealed class DeskHands
{
    internal bool Enabled;
    private const float MouseLimitX=28,MouseStart=-MouseLimitX*.5f;
    internal float KeyX,KeyY,MouseX=MouseStart;
    private float target,age=1,released=1,dx=MouseStart,travelStart,travelGoal;
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
            held.RemoveAt(index);
            if(held.Count>0)target=held[^1].X;else released=0;
        }else if(index<0){
            target=510+col*541-930;held.Add((id,target));age=0;
        }
    }
    internal void Cursor(float x){dx=Math.Clamp(dx+x*.15f,-MouseLimitX,MouseLimitX);}
    internal void Step(float dt){
        var w=1-MathF.Exp(-dt*21);age=Math.Min(1,age+dt);
        if(held.Count==0)released=Math.Min(1,released+dt);
        bool returning=held.Count==0&&released>=.5f;
        var goal=returning?0:target;
        if(goal!=travelGoal){travelStart=KeyX;travelGoal=goal;}
        KeyX+=(goal-KeyX)*(returning?1-MathF.Exp(-dt*12):w);
        var distance=goal-travelStart;
        var progress=distance==0?1:Math.Clamp((KeyX-travelStart)/distance,0,1);
        var lift=Math.Min(38,Math.Abs(distance)*.22f)*4*progress*(1-progress);
        var tap=age<.13f?8*MathF.Sin(MathF.PI*age/.13f):0;
        KeyY+=(tap-lift-KeyY)*w;MouseX+=(dx-MouseX)*w;
    }
    internal void Reset(){held.Clear();target=KeyX=KeyY=travelStart=travelGoal=0;age=released=1;}
}
