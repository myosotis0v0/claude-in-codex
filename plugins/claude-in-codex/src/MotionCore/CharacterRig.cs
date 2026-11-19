using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionLab;

internal sealed record RigPoint(float X,float Y);
internal sealed record BoneSpec(string Name,int Parent,float X,float Y);
internal sealed record MeshSpec(int Columns,int Rows,string Weights,int Mid=0,int Tip=0,bool BodyBlend=false);
internal sealed record WeightBand(float Start,float Length)
{
    internal float Linear(float value)=>Math.Clamp((value-Start)/Length,0,1);
    internal float Smooth(float value){var v=Linear(value);return v*v*(3-2*v);}
}
internal sealed record TorsoWeights(float X,float HalfWidth);
internal sealed record FrontWeights(WeightBand Bend,WeightBand Tip,WeightBand Body);
internal sealed record WeightField(int Bone,float X,float Y,float Width,float Height,float Gain);
internal sealed record LegacyWeights(float HeadOnlyAbove,WeightBand Body,float BodyGain,float HeadGain,int MaxInfluences,WeightField[] Fields);
internal sealed record WeightSettings(TorsoWeights Torso,WeightBand Accessory,FrontWeights FrontHair,WeightBand TouchTip,WeightBand RearFlow,LegacyWeights LegacyHair);

// Schema v1 is a recipe for the existing rig channels, not a general-purpose rig importer.
internal sealed record CharacterRig(int SchemaVersion,BoneSpec[] Bones,RigPoint TouchPivot,Dictionary<string,MeshSpec> Meshes,
    WeightSettings Weights,string[] NormalLayers,string[] TouchLayers,string[] DeskLayers)
{
    internal const string FileName="character-rig.json";
    internal static CharacterRig Default=>Template.Value;
    private static readonly JsonSerializerOptions Options=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters=true
    };
    private static readonly Lazy<CharacterRig> Template=new(()=>{
        using var stream=typeof(CharacterRig).Assembly.GetManifestResourceStream(FileName)
            ??throw new InvalidDataException("Missing embedded character rig template.");
        return Read(stream);
    });
    internal static CharacterRig Load(string root,bool required=false)
    {
        var path=Path.Combine(root,FileName);
        if(!required&&!File.Exists(path))return Template.Value;
        try{using var stream=File.OpenRead(path);return Read(stream);}
        catch(JsonException ex){throw new InvalidDataException("Malformed character rig configuration.",ex);}
    }
    private static CharacterRig Read(Stream stream)
    {
        var rig=JsonSerializer.Deserialize<CharacterRig>(stream,Options)??throw new InvalidDataException("Empty character rig.");
        rig.Validate();
        return rig;
    }
    private void Validate()
    {
        static void Require([DoesNotReturnIf(false)] bool ok,string field){if(!ok)throw new InvalidDataException("Invalid character rig: "+field);}
        string[] slots=["root","head","bang-left","bang-center","bang-right","side-left","side-left-tip","side-right",
            "side-right-tip","ribbon-root","ribbon-tip","flower","breath","front-left-mid","front-left-tip","front-center-mid",
            "front-center-tip","front-right-mid","front-right-tip"];
        Require(SchemaVersion==1,"schemaVersion");
        Require(Bones!=null&&Bones.Select(b=>b?.Name).SequenceEqual(slots),"bone channels");
        for(int i=0;i<Bones.Length;i++)
            Require((i==0?Bones[i].Parent==-1:Bones[i].Parent>=0&&Bones[i].Parent<i)&&float.IsFinite(Bones[i].X)&&float.IsFinite(Bones[i].Y),"bone transform");
        Require(TouchPivot!=null&&float.IsFinite(TouchPivot.X)&&float.IsFinite(TouchPivot.Y),"touchPivot");
        string[] meshes=["hair","accessory","neck","clothes-paws","rear-hair","hair-front-left","hair-front-center","hair-front-right","hair-backing","hair-curtain"];
        Require(Meshes!=null&&Meshes.Keys.ToHashSet().SetEquals(meshes),"mesh names");
        foreach(var mesh in Meshes.Values){
            Require(mesh is {Columns:>0 and <=128,Rows:>0 and <=128}&&mesh.Weights is ("legacyHair" or "accessory" or "torso" or "frontHair" or "head"),"mesh dimensions or weight profile");
            if(mesh.Weights=="frontHair")Require(mesh.Mid>=0&&mesh.Mid<Bones.Length&&mesh.Tip>=0&&mesh.Tip<Bones.Length,"front hair binding");
        }
        foreach(var key in new[]{"hair-front-left","hair-front-center","hair-front-right"})
            Require(Meshes[key].Weights=="frontHair","touch hair binding");
        Require(Weights?.Torso?.HalfWidth>0&&Weights.FrontHair!=null&&Weights.LegacyHair!=null,"weight settings");
        foreach(var band in new[]{Weights.Accessory,Weights.FrontHair.Bend,Weights.FrontHair.Tip,Weights.FrontHair.Body,Weights.TouchTip,Weights.RearFlow,Weights.LegacyHair.Body})
            Require(band!=null&&band.Length>0,"weight band");
        var legacy=Weights.LegacyHair;
        Require(legacy.MaxInfluences is >0 and <=8&&legacy.BodyGain>0&&legacy.HeadGain>0&&legacy.Fields!=null&&
            legacy.Fields.All(f=>f!=null&&f.Bone>=0&&f.Bone<Bones.Length&&f.Width>0&&f.Height>0&&f.Gain>=0),"legacy hair weights");
        void Layers(string[] actual,params string[] expected)=>Require(actual!=null&&actual.Length==expected.Length&&actual.ToHashSet().SetEquals(expected),"layer order");
        Layers(NormalLayers,"rear-hair","neck","hair-backing","hair-curtain","menu-neck","cape-shoulders","chest-ornament",
            "face","hair-front-left","hair-front-right","hair-front-center","hair-cap","hair","accessory");
        Layers(TouchLayers,"rear-hair","neck","hair-backing","hair-curtain","cape-shoulders","chest-ornament","face","features",
            "hair-front-left","hair-front-right","hair-front-center","hair-cap","accessory");
        Layers(DeskLayers,"desk-mouse","desk-keyboard","mouse-paw","keyboard-paw");
    }

    internal Bone[] CreateBones()=>Bones.Select(b=>new Bone(b.Name,b.Parent,new Vector2(b.X,b.Y))).ToArray();
}
