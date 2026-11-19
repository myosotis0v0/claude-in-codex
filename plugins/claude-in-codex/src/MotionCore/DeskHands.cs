namespace MotionLab;

[Flags]
internal enum FingerPose { Rest=0,Index=1,Little=2,Middle=4 }

internal readonly record struct DeskPose(bool Enabled,float MouseX,float PointerX,float PointerY,bool PointerAway,
    bool PointerContact,FingerPose MousePose,float KeyboardX,float KeyboardY,bool KeyboardContact);

internal sealed class DeskHands
{
    internal bool Enabled;
    private const float MouseLimitX=28,MouseStart=-MouseLimitX*.5f;
    internal const float DeskShiftX=-54,KeyboardHome=98;
    // Centers and width measured from the visible paw silhouettes on the source canvas.
    internal const float PointerCenter=318,KeyboardCenter=925,MinimumGap=252*2/3f;
    internal float MouseX=MouseStart;
    private float dx=MouseStart;
    private bool mouseLocked;
    private FingerPose heldButtons,pendingButtons;
    private float wheelTime;
    private bool wheelPending;
    internal FingerPose MousePose {get;private set;}
    internal void MouseButtons(int flags,int delta){
        if(!Enabled||Pointer.Away)return;
        if((flags&1)!=0){heldButtons|=FingerPose.Index;pendingButtons|=FingerPose.Index;}
        if((flags&2)!=0)heldButtons&=~FingerPose.Index;
        if((flags&4)!=0){heldButtons|=FingerPose.Little;pendingButtons|=FingerPose.Little;}
        if((flags&8)!=0)heldButtons&=~FingerPose.Little;
        if((flags&0x400)!=0&&delta!=0)wheelPending=true;
    }
    private void ClearFingers(){
        MousePose=heldButtons=pendingButtons=FingerPose.Rest;wheelTime=0;wheelPending=false;
    }
    internal readonly TypingHand Keyboard=new(KeyboardHome), Pointer=new(MouseStart);
    internal (float pointer,float keyboard) DrawOffsets(){
        float missing=Math.Max(0,MinimumGap-(KeyboardCenter+Keyboard.X-PointerCenter-Pointer.X));
        float share=!Pointer.Away?0:!Keyboard.Away?1:.5f;
        return (Pointer.X-share*missing,Keyboard.X+(1-share)*missing);
    }
    internal DeskPose Snapshot(){
        var (pointer,keyboard)=DrawOffsets();
        return new(Enabled,MouseX,pointer,Pointer.Y,Pointer.Away,Pointer.Contact,MousePose,keyboard,Keyboard.Y,Keyboard.Contact);
    }
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
        bool right=extended||scan is (>=0x08 and <=0x0e) or (>=0x16 and <=0x1c)
            or (>=0x24 and <=0x28) or 0x2b or (>=0x31 and <=0x36)
            or (>=0x40 and <=0x44) or (>=0x47 and <=0x53) or 0x57 or 0x58;
        var hand=right?Pointer:Keyboard;
        if(right&&!up&&!Pointer.Away){dx=MouseX;ClearFingers();}
        hand.Key(scan|(extended?256:0),1051-col*541-(right?315:930),up);
    }
    internal void Cursor(float x){if(!mouseLocked&&!Pointer.Away)dx=Math.Clamp(dx+x*.15f,-MouseLimitX,MouseLimitX);}
    internal void Step(float dt,bool lockMouse=false){
        // Anchor to the last displayed position, not the pending smoothed target.
        mouseLocked=lockMouse;
        if(mouseLocked)dx=MouseX;
        wheelTime=Math.Max(0,wheelTime-dt);
        if(wheelPending&&wheelTime==0){wheelTime=.14f;wheelPending=false;}
        MousePose=heldButtons|pendingButtons|(wheelTime>.06f?FingerPose.Middle:FingerPose.Rest);
        pendingButtons=FingerPose.Rest;
        MouseX+=(dx-MouseX)*(1-MathF.Exp(-dt*21));
        Keyboard.Step(dt,KeyboardHome);Pointer.Step(dt,MouseX);
    }
    internal void Reset(){ClearFingers();Keyboard.Reset(KeyboardHome);Pointer.Reset(MouseX);}
}

internal sealed class TypingHand(float homeX)
{
    internal float X=homeX,Y;
    internal bool Contact {get;private set;}
    internal bool Away {get;private set;}
    private bool pendingStrike,liftFrame;
    private float target,released=1,travelStart=homeX,travelGoal=homeX;
    private readonly System.Collections.Generic.List<(int Id,float X)> held=new();
    internal void Key(int id,float x,bool up){
        int index=held.FindIndex(k=>k.Id==id);
        if(up){
            if(index<0)return;
            bool current=index==held.Count-1;
            held.RemoveAt(index);
            if(held.Count==0)released=0;
            else if(current)Reach(held[^1].X);
        }else if(index<0){
            Away=true;Reach(x);held.Add((id,target));
        }
    }
    private void Reach(float x){
        // Only the newest strike is pending. Show a lift between consecutive contacts.
        target=x;liftFrame=Contact;pendingStrike=true;
    }
    internal void Step(float dt,float home){
        if(!Away){X=travelGoal=travelStart=home;Y=0;return;}
        if(held.Count==0)released=Math.Min(1,released+dt);
        bool returning=held.Count==0&&released>=.5f;
        var goal=returning?home:target;
        if(goal!=travelGoal){travelStart=X;travelGoal=goal;}
        X+=(goal-X)*(1-MathF.Exp(-dt*(returning?12:34)));
        var distance=goal-travelStart;
        var progress=distance==0?1:Math.Clamp((X-travelStart)/distance,0,1);
        var arc=Math.Clamp((Math.Abs(distance)-48)*.08f,0,16);
        Y=-arc*4*progress*(1-progress);
        Contact=!returning&&!liftFrame&&(held.Count!=0||pendingStrike)&&Math.Abs(goal-X)<20;
        if(Contact)pendingStrike=false;
        liftFrame=false;
        if(returning&&Math.Abs(X-home)<.3f)Reset(home);
    }
    internal void Reset(float home){held.Clear();target=X=travelStart=travelGoal=home;Y=0;released=1;Away=Contact=pendingStrike=liftFrame=false;}
}
