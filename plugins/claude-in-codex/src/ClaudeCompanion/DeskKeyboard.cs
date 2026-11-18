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
    private struct Packet{public Header Header;public ushort Scan,Flags,Reserved,VirtualKey;public uint Message,Extra;}
    [DllImport("user32.dll")]private static extern bool RegisterRawInputDevices(ref Device device,uint count,uint size);
    [DllImport("user32.dll")]private static extern uint GetRawInputData(nint raw,uint command,out Packet packet,ref uint size,uint headerSize);
    private bool registered;
    private long retry;
    internal void Enable(nint hwnd,bool enabled){
        if(registered==enabled||enabled&&Environment.TickCount64<retry)return;
        var device=new Device{Page=1,Usage=6,Flags=enabled?0x100u:1u,Target=enabled?hwnd:0};
        if(RegisterRawInputDevices(ref device,1,(uint)Marshal.SizeOf<Device>()))registered=enabled;
        else retry=Environment.TickCount64+2000;
    }
    internal void Receive(nint raw,DeskHands hands){
        if(!registered)return;
        uint size=(uint)Marshal.SizeOf<Packet>();
        if(GetRawInputData(raw,0x10000003,out var packet,ref size,(uint)Marshal.SizeOf<Header>())==size&&
            packet.Header.Type==1&&(packet.Flags&5)==0)
            hands.Key(packet.Scan,(packet.Flags&2)!=0);
    }
}
