namespace MotionLab;

internal static class Acting
{
    static float Ease(double t){var u=(float)Math.Clamp(t,0,1);return u*u*(3-2*u);}
    internal static float Beat(double t,double start,double peak,double release,double end)=>
        Ease((t-start)/(peak-start))*(1-Ease((t-release)/(end-release)));

    internal static Pose Apply(string id,Pose p,double t)
    {
        float B(double a,double b,double c,double d)=>Beat(t,a,b,c,d);
        float tilt=0,x=0,y=0;
        switch(id)
        {
            case "thinking":
                var listen=B(.1,.65,1.15,1.65);var drift=B(1.1,2.35,3.45,4.12);
                var catchUp=B(3.72,4.02,4.10,4.65);
                tilt=3*listen-5.8f*drift+2.2f*catchUp;x=-18*drift;y=15*drift-9*catchUp;
                p=p with{EyeRoll=drift,
                    EyeLeft=Math.Min(p.EyeLeft,1-.29f*drift),EyeRight=Math.Min(p.EyeRight,1-.18f*drift),
                    BrowLeft=p.BrowLeft-7*listen,BrowRight=p.BrowRight+6*drift,
                    Mouth="u",MouthOpen=.72f,MouthMix=drift};
                break;
            case "complete":
                return p with{CelId=id,CelTime=t};
            case "complete-smile":
                var dip=B(.03,.18,.22,.47);var pop=B(.2,.46,.52,.87);var land=B(.55,.84,.91,1.25);
                tilt=-2*dip+4.8f*pop-1.8f*land;y=20*dip-34*pop+12*land;x=9*pop;
                p=p with{Mouth="happy",MouthMix=Math.Max(p.MouthMix,pop),Blush=.35f*pop,
                    EyeLeft=p.EyeLeft*(1-pop),EyeRight=p.EyeRight*(1-pop),SoftSmileEyes=pop,SmileEyes=pop};break;
            case "pat":
                var press=B(.04,.36,.45,1.0);var nuzzle=B(.46,1.45,2.05,3.05);
                var again=B(2.56,3.35,3.6,4.65);
                tilt=2.5f*press-5.8f*nuzzle+3.6f*again;x=-25*nuzzle+14*again;y=20*press-8*nuzzle;
                p=p with{Blush=Math.Max(p.Blush,.68f*nuzzle)};break;
            case "sleepy":
                var sag=B(.4,2.85,3.14,3.55);var jolt=B(3.16,3.46,3.55,4.12);var settle=B(3.65,4.03,4.23,4.95);
                tilt=6.8f*sag-4*jolt+1.8f*settle;y=31*sag-24*jolt+6*settle;x=18*sag;
                p=p with{EyeLeft=p.EyeLeft*(1-sag)*(1-jolt)+1.24f*jolt,
                    EyeRight=p.EyeRight*(1-sag)*(1-jolt)+1.24f*jolt,BrowY=p.BrowY-24*jolt,
                    Mouth="yawn",MouthMix=1,MouthOpen=.62f*sag+jolt*(1-.62f*sag)};break;
            case "wink":
                var prep=B(0,.09,.11,.28);var strike=B(.1,.32,.75,1.35);
                tilt=2.3f*prep-5.4f*strike;y=9*prep-15*strike;x=-16*strike;
                p=p with{BrowRight=p.BrowRight+8*strike,Blush=p.Blush+.38f*strike};break;
            case "menu":
                p=p with{BrowLeft=-7*p.MenuPitchWeight,BrowRight=3*p.MenuPitchWeight};
                break;
            default:return p;
        }
        return p with{HeadAngle=p.HeadAngle+tilt,HeadX=p.HeadX+x,HeadY=p.HeadY+y,CelId=id,CelTime=t};
    }

}
