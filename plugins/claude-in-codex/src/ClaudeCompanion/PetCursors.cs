using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace ClaudeCompanion;

internal sealed class PetCursors : IDisposable
{
    private readonly Cursor? hover,press;

    internal PetCursors(string? folder=null)
    {
        folder??=Path.Combine(AppContext.BaseDirectory,"assets","cursors");
        hover=Load(Path.Combine(folder,"hover.cur"));
        press=Load(Path.Combine(folder,"press.cur"));
    }

    internal Cursor For(bool locked,bool touchingHead)=>
        !locked?Cursors.SizeAll:(touchingHead?press:null)??hover??Cursors.Hand;

    private static Cursor? Load(string path)
    {
        try{return new Cursor(path,scaleWithDpi:true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception or InvalidOperationException)
        {return null;}
    }

    public void Dispose(){press?.Dispose();hover?.Dispose();}
}
