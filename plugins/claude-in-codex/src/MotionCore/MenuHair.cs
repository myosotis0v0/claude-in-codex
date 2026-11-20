using System.IO;

namespace MotionLab;

internal sealed class MenuHair : IDisposable
{
    internal const string DirectoryName="menu-hair";
    private static readonly string[] Names=["hair-front-left","hair-front-center","hair-front-right","hair-backing"];
    internal TexturePart[] Parts {get;}
    internal Mesh[] Meshes {get;}
    internal TexturePart Cap {get;}
    internal TexturePart Crown {get;}
    internal long TextureBytes=>Parts.Sum(p=>(long)p.Bitmap.ByteCount)+Cap.Bitmap.ByteCount+Crown.Bitmap.ByteCount;

    internal MenuHair(CharacterRig profile,string root,Mesh backing,bool highQuality)
    {
        TexturePart Load(string name)=>new(Path.Combine(root,name+".png"));
        var loaded=new List<TexturePart>();
        TexturePart Track(string name){var part=Load(name);loaded.Add(part);return part;}
        try
        {
            Parts=Names.Select(Track).ToArray();Cap=Track("hair-cap");Crown=Track("hair-crown-rear");
            Meshes=[new(profile,Names[0]),new(profile,Names[1]),new(profile,Names[2]),
                new Mesh(profile,Names[3],backing.BottomOverscan){RearFlow=backing.RearFlow}];
            for(int i=0;i<Meshes.Length;i++)
            {
                var part=Parts[i];Meshes[i].PrepareTexture(part.Bitmap,part.X,part.Y,highQuality);
            }
        }
        catch
        {
            if(Meshes!=null)foreach(var mesh in Meshes)mesh.Dispose();
            foreach(var part in loaded)part.Bitmap.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach(var mesh in Meshes)mesh.Dispose();
        foreach(var part in Parts)part.Bitmap.Dispose();
        Cap.Bitmap.Dispose();Crown.Bitmap.Dispose();
    }
}
