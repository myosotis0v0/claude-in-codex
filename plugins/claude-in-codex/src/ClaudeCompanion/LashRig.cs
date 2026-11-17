using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeCompanion;

internal sealed class LashRig
{
    private sealed class Eye(string name,Rect rect)
    {
        internal string Name {get;}=name;
        internal Rect Rect {get;}=rect;
        internal Dictionary<int,(ImageBrush Mask,BitmapSource Lashes)> Poses {get;}=[];
    }
    private readonly string root;
    private readonly Dictionary<bool,Eye> eyes=[];
    internal BitmapSource SkinArtwork {get;}
    internal int Steps {get;}
    internal LashRig(string assetRoot)
    {
        root=Path.Combine(assetRoot,"lids");
        SkinArtwork=PetView.Load(Path.Combine(root,"skin-base.png"));
        using var rig=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"rig.json")));
        Steps=rig.RootElement.GetProperty("Steps").GetInt32();
        foreach(var item in rig.RootElement.GetProperty("Eyes").EnumerateArray())
        {
            var name=item.GetProperty("Name").GetString()!;var rect=item.GetProperty("Rect");
            eyes.Add(name=="left",new Eye(name,new Rect(rect[0].GetDouble(),rect[1].GetDouble(),rect[2].GetDouble(),rect[3].GetDouble())));
        }
    }
    internal int Pose(double normalizedY)=>Steps+(int)Math.Round(Math.Clamp(normalizedY,-1,1)*Steps);
    internal Rect Region(bool viewerLeft)=>eyes[viewerLeft].Rect;
    internal (ImageBrush Mask,BitmapSource Lashes) Get(bool viewerLeft,int pose)
    {
        var eye=eyes[viewerLeft];
        if(eye.Poses.TryGetValue(pose,out var cached))return cached;
        var mask=new ImageBrush(PetView.Load(Path.Combine(root,$"{eye.Name}-mask-{pose:D2}.png")))
        {
            Stretch=Stretch.Fill,ViewportUnits=BrushMappingMode.Absolute,Viewport=eye.Rect,TileMode=TileMode.None
        };
        mask.Freeze();
        cached=(mask,PetView.Load(Path.Combine(root,$"{eye.Name}-pose-{pose:D2}.png")));
        eye.Poses.Add(pose,cached);return cached;
    }
}
