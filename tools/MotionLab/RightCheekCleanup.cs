using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace MotionLab;

internal static class RightCheekCleanup
{
    // Lower silhouette of the existing upper curl, not the old face-outline spur attached below it.
    private static readonly (float X,float Y)[] Edge=[(834,917),(840,921),(850,929),(860,935),(870,939),(880,941),
        (890,942),(900,941),(910,939),(920,935),(930,932),(940,928),(950,923),(960,916)];
    private static float Boundary(float x)
    {
        for(var i=1;i<Edge.Length;i++)if(x<=Edge[i].X)
        {
            var a=Edge[i-1];var b=Edge[i];var t=(x-a.X)/(b.X-a.X);
            var left=i>1?(b.Y-Edge[i-2].Y)/(b.X-Edge[i-2].X):(b.Y-a.Y)/(b.X-a.X);
            var right=i<Edge.Length-1?(Edge[i+1].Y-a.Y)/(Edge[i+1].X-a.X):(b.Y-a.Y)/(b.X-a.X);
            var width=b.X-a.X;return (2*t*t*t-3*t*t+1)*a.Y+(t*t*t-2*t*t+t)*left*width+(-2*t*t*t+3*t*t)*b.Y+(t*t*t-t*t)*right*width;
        }
        return Edge[^1].Y;
    }
    internal static void Build(string repository)
    {
        var source=Path.Combine(repository,"assets","claude-motion-v3","layers");
        var root=Path.Combine(repository,"assets","claude-motion-v4");var target=Path.Combine(root,"layers");Directory.CreateDirectory(target);
        foreach(var file in Directory.EnumerateFiles(source,"*.png"))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
        using var original=Assets.Load(Path.Combine(source,"hair-front-right.png"));using var front=original.Copy();
        var removed=0;
        for(var y=930;y<1030;y++)for(var x=834;x<=960;x++)
        {
            var pixel=original.GetPixel(x,y);if(pixel.Alpha==0)continue;
            var alpha=(byte)Math.Round(pixel.Alpha*Math.Clamp(Boundary(x)-y+.5f,0,1));
            if(alpha!=pixel.Alpha){front.SetPixel(x,y,alpha==0?SKColors.Transparent:pixel.WithAlpha(alpha));removed++;}
        }
        Assets.Save(front,Path.Combine(target,"hair-front-right.png"));
        var protectedFiles=Directory.EnumerateFiles(source,"*.png").Where(f=>Path.GetFileName(f)!="hair-front-right.png").ToArray();
        if(protectedFiles.Any(f=>!SHA256.HashData(File.ReadAllBytes(f)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(target,Path.GetFileName(f)))))))
            throw new InvalidDataException("Unrelated material changed.");
        var outside=0;var added=0;
        for(var y=0;y<1254;y++)for(var x=0;x<1254;x++)
        {
            var a=original.GetPixel(x,y);var b=front.GetPixel(x,y);
            if(a!=b&&(x<834||x>960||y<930||y>=1030))outside++;
            if(b.Alpha>a.Alpha)added++;
        }
        if(removed==0||outside!=0||added!=0)throw new InvalidDataException("Cleanup must only remove the isolated legacy cheek spur within the local ROI.");
        FrontHairAudit.CheckComponents(root);
        File.WriteAllText(Path.Combine(root,"cleanup-report.json"),JsonSerializer.Serialize(new{Success=true,ChangedPixels=removed,OutsideRoiChanges=outside,AddedPixels=added,
            ProtectedAssets=protectedFiles.Length,Roi=new{X=834,Y=930,Width=127,Height=100},
            Scope="Source-space ownership-mask correction only. Remove the old cheek-outline spur, retain the complete upper curl. No generated/repainted artwork and no new invented strand."},Assets.Json));
        Console.WriteLine($"Right cheek: {removed} local pixels cleaned; {protectedFiles.Length} assets unchanged.");
    }
}
