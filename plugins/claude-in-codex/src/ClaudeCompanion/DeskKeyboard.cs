using System.Runtime.InteropServices;
using MotionLab;

namespace ClaudeCompanion;
internal sealed class DeskKeyboard
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Device{public ushort Page,Usage;public uint Flags;public nint Target;}
    [StructLayout(LayoutKind.Sequential)]
    private struct Header{public uint Type,Size;public nint Device,Param;}
    [StructLayout(LayoutKind.Sequential)]
    private struct Packet{public Header Header;public Payload Data;}
    [StructLayout(LayoutKind.Explicit,Size=24)]
    private struct Payload{
        [FieldOffset(0)] public ushort Scan;
        [FieldOffset(2)] public ushort KeyFlags;
        [FieldOffset(4)] public ushort MouseFlags;
        [FieldOffset(6)] public short WheelDelta;
    }
    [DllImport("user32.dll")]private static extern bool RegisterRawInputDevices([In] Device[] devices,uint count,uint size);
    [DllImport("user32.dll")]private static extern uint GetRawInputData(nint raw,uint command,out Packet packet,ref uint size,uint headerSize);
    private bool registered;
    private long retry;
    internal void Enable(nint hwnd,bool enabled){
        if(registered==enabled||enabled&&Environment.TickCount64<retry)return;
        Device For(ushort usage)=>new(){Page=1,Usage=usage,Flags=enabled?0x100u:1u,Target=enabled?hwnd:0};
        if(RegisterRawInputDevices([For(6),For(2)],2,(uint)Marshal.SizeOf<Device>()))registered=enabled;
        else retry=Environment.TickCount64+2000;
    }
    internal void Receive(nint raw,DeskHands hands){
        if(!registered)return;
        uint size=(uint)Marshal.SizeOf<Packet>();
        var header=(uint)Marshal.SizeOf<Header>();
        var read=GetRawInputData(raw,0x10000003,out var packet,ref size,header);
        if(read==uint.MaxValue||read!=packet.Header.Size)return;
        var data=packet.Data;
        if(packet.Header.Type==1&&read>=header+16&&(data.KeyFlags&4)==0)
            hands.Key(data.Scan,(data.KeyFlags&2)!=0,(data.KeyFlags&1)!=0);
        else if(packet.Header.Type==0&&read>=header+24&&data.MouseFlags!=0)
            hands.MouseButtons(data.MouseFlags,data.WheelDelta);
    }
}
