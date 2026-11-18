namespace MotionLab;

internal sealed class DeskHands
{
    internal bool Enabled;
    internal float KeyX,KeyY,MouseX,MouseY;
    private float target,age=1,dx,dy;
    internal static float? Column(int sc,bool extended){
        if(extended)return sc switch{0x1c or 0x48 or 0x50=>.91f,0x4b=>.84f,0x4d=>1,_=>null};
        return sc switch{
            >=0x02 and <=0x0d=>(sc-2)/13f, >=0x10 and <=0x1b=>(sc-0x10+.5f)/13,
            >=0x1e and <=0x28=>(sc-0x1e+.8f)/13, >=0x2c and <=0x35=>(sc-0x2c+1.3f)/13,
            0x01 or 0x0f or 0x29 or 0x2a or 0x3a=>0,0x39=>.47f,
            0x0e or 0x1c or 0x2b or 0x36=>1,>=0x3b and <=0x44=>(sc-0x3b)/11f,
            0x57 or 0x58 or >=0x47 and <=0x53=>1,_=>null};
    }
    internal void Key(int scan,bool extended){
        if(Enabled&&Column(scan,extended) is float col){target=510+col*541-930;age=0;}
    }
    internal void Cursor(float x,float y){dx=Math.Clamp(dx+x*.15f,-14,14);dy=Math.Clamp(dy+y*.1f,-7,7);}
    internal void Step(float dt){
        var w=1-MathF.Exp(-dt*21);age=Math.Min(1,age+dt);
        KeyX+=((age<.28f?target:0)-KeyX)*w;
        var tap=age<.13f?8*MathF.Sin(MathF.PI*age/.13f):0;
        KeyY+=(tap-KeyY)*w;MouseX+=(dx-MouseX)*w;MouseY+=(dy-MouseY)*w;
        var decay=MathF.Exp(-dt*5);dx*=decay;dy*=decay;
    }
    internal void Reset(){target=KeyX=KeyY=MouseX=MouseY=dx=dy=0;age=1;}
}
