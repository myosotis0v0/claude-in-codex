using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Offline asset packaging: recover the original lash pixels over a generated skin fill.
internal static class Program
{
    private const int Canvas=1254;
    private const int Steps=16;
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("Usage: LashAssetBuilder <v2-assets> <generated-clean-base.png> <output-directory>");
        var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
        var original=Load(Path.Combine(source,"base.png"));var generated=Load(args[1]);
        if(original.Width!=Canvas||original.Height!=Canvas||generated.Width!=Canvas||generated.Height!=Canvas)
            throw new InvalidOperationException("Source and generated underlay must be aligned 1254-square images.");
        var eyes=new List<object>();var checks=new List<string>();var skinBase=(byte[])original.Pixels.Clone();
        foreach(var spec in new[]{new Spec("left",270,710,295,230,279,548),new Spec("right",700,710,290,230,713,978)})
        {
            var skin=new byte[spec.W*spec.H*4];var lash=new byte[skin.Length];var crease=new byte[skin.Length];var foreground=new byte[skin.Length];
            var aperture=new byte[skin.Length];var sourceMask=Load(Path.Combine(source,$"{spec.Name}-motion-mask.png"));
            var separated=0;
            for(var y=0;y<spec.H;y++)for(var x=0;x<spec.W;x++)
            {
                var sx=x+spec.X;var sy=y+spec.Y;var i=(y*spec.W+x)*4;var p=(sy*Canvas+sx)*4;
                original.Pixels.AsSpan(p,4).CopyTo(skin.AsSpan(i,4));
                var cleanSkin=IsSkin(generated.Pixels,p);
                var difference=Math.Max(Math.Abs(original.Pixels[p]-generated.Pixels[p]),
                    Math.Max(Math.Abs(original.Pixels[p+1]-generated.Pixels[p+1]),Math.Abs(original.Pixels[p+2]-generated.Pixels[p+2])));
                var inLashBand=sy>=742&&sy<=794&&sx>=spec.Start&&sx<=spec.End;
                var inCreaseBand=sy>=727&&sy<747&&(spec.Name=="left"?sx>=416&&sx<=524:sx>=728&&sx<=830);
                var remove=cleanSkin&&difference>16&&original.Pixels[p+2]<240&&(inLashBand||inCreaseBand);
                if(remove)
                {
                    generated.Pixels.AsSpan(p,3).CopyTo(skin.AsSpan(i,3));
                    generated.Pixels.AsSpan(p,3).CopyTo(skinBase.AsSpan(p,3));
                    var layer=sy<747?crease:lash;
                    original.Pixels.AsSpan(p,4).CopyTo(layer.AsSpan(i,4));layer[i+3]=255;separated++;
                }
                // Hair, brows and face outlines remain a fixed matte, not a child of the lash warp.
                if(!cleanSkin&&!remove)original.Pixels.AsSpan(p,4).CopyTo(foreground.AsSpan(i,4));
                var a=sourceMask.Pixels[p];aperture[i]=aperture[i+1]=aperture[i+2]=255;aperture[i+3]=a;
            }
            // Carry the original antialiased edge with the lash instead of leaving a pale ghost behind.
            var seeds=new bool[spec.W*spec.H];
            for(var i=0;i<seeds.Length;i++)seeds[i]=lash[i*4+3]>0||crease[i*4+3]>0;
            for(var y=2;y<spec.H-2;y++)for(var x=2;x<spec.W-2;x++)
            {
                var i=(y*spec.W+x)*4;var p=((y+spec.Y)*Canvas+x+spec.X)*4;
                if(seeds[y*spec.W+x]||!IsSkin(generated.Pixels,p))continue;
                var near=false;
                for(var dy=-2;dy<=2;dy++)for(var dx=-2;dx<=2;dx++)near|=seeds[(y+dy)*spec.W+x+dx];
                if(!near)continue;
                generated.Pixels.AsSpan(p,3).CopyTo(skin.AsSpan(i,3));generated.Pixels.AsSpan(p,3).CopyTo(skinBase.AsSpan(p,3));
                var layer=y+spec.Y<747?crease:lash;original.Pixels.AsSpan(p,4).CopyTo(layer.AsSpan(i,4));layer[i+3]=255;separated++;
            }
            if(separated<3000)throw new InvalidOperationException($"{spec.Name}: lash extraction too small ({separated}).");
            Save(skin,spec,$"{spec.Name}-skin.png",output);Save(lash,spec,$"{spec.Name}-lashes.png",output);
            Save(crease,spec,$"{spec.Name}-crease.png",output);Save(foreground,spec,$"{spec.Name}-foreground.png",output);
            Save(aperture,spec,$"{spec.Name}-aperture.png",output);
            var neutral=Over(Over((byte[])skin.Clone(),crease),lash);var mismatch=0;
            for(var y=0;y<spec.H;y++)for(var x=0;x<spec.W;x++)for(var c=0;c<3;c++)
                if(neutral[(y*spec.W+x)*4+c]!=original.Pixels[((y+spec.Y)*Canvas+x+spec.X)*4+c])mismatch++;
            if(mismatch!=0)throw new InvalidOperationException($"{spec.Name}: neutral reconstruction mismatch: {mismatch}.");
            checks.Add($"{spec.Name}: {separated} original lash/crease pixels separated; neutral RGB reconstructed byte-for-byte");
            for(var pose=-Steps;pose<=Steps;pose++)
            {
                var value=pose/(double)Steps;var amplitude=value*(value<0?8:12);
                var warpedLash=Warp(lash,spec,amplitude,1,false);
                var warpedCrease=Warp(crease,spec,amplitude,0.3,false);
                var combined=Over(warpedCrease,warpedLash);
                var mask=Warp(aperture,spec,amplitude,1,true);
                for(var i=0;i<mask.Length;i+=4)
                {
                    if(foreground[i+3]>0)
                    {
                        combined[i+3]=0;mask[i+3]=0;
                    }
                }
                // At neutral use the exact original extraction and mask, including their edge pixels.
                if(pose==0){combined=Over((byte[])crease.Clone(),lash);mask=(byte[])aperture.Clone();}
                Save(combined,spec,$"{spec.Name}-pose-{pose+Steps:D2}.png",output);
                Save(mask,spec,$"{spec.Name}-mask-{pose+Steps:D2}.png",output);
            }
            eyes.Add(new{Name=spec.Name,Rect=new[]{spec.X,spec.Y,spec.W,spec.H},Start=spec.Start,End=spec.End});
        }
        Save(skinBase,new Spec("skin-base",0,0,Canvas,Canvas,0,Canvas),"skin-base.png",output);
        File.WriteAllText(Path.Combine(output,"rig.json"),JsonSerializer.Serialize(new{Version=1,Steps,Up=8,Down=12,CreaseGain=0.3,Eyes=eyes,
            Sources=new{Original="../base.png",Underlay="generated-clean-base.png"},
            References=new[]{"https://docs.live2d.com/en/cubism-editor-manual/deformpath/","https://docs.live2d.com/en/cubism-editor-tutorials/expression/"}},new JsonSerializerOptions{WriteIndented=true}));
        var savedUnderlay=Path.Combine(output,"generated-clean-base.png");
        if(!Path.GetFullPath(args[1]).Equals(savedUnderlay,StringComparison.OrdinalIgnoreCase))File.Copy(args[1],savedUnderlay,true);
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{Success=true,Checks=checks,Scope="Layer extraction and pose baking only; source artwork is never overwritten."},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var check in checks)Console.WriteLine(check);
    }

    private static bool IsSkin(byte[] data,int p)=>data[p+2]>210&&data[p+1]>165&&data[p]>155&&data[p+3]>240;
    private static double Bow(double x,Spec s)
    {
        var t=Math.Clamp((x+s.X-s.Start)/(s.End-s.Start),0,1);
        return Math.Pow(Math.Sin(Math.PI*t),2);
    }
    private static byte[] Warp(byte[] source,Spec spec,double amplitude,double gain,bool mask)
    {
        var result=new byte[source.Length];
        for(var y=0;y<spec.H;y++)for(var x=0;x<spec.W;x++)
        {
            var displacement=amplitude*gain*Bow(x,spec);
            // The bottom aperture boundary stays anchored; only its upper boundary follows the lid.
            var sy=mask?(y+spec.Y-789-displacement)/(929-789-displacement)*(929-789)+789-spec.Y:y-displacement;
            var y0=(int)Math.Floor(sy);var blend=sy-y0;
            var ia=y0>=0&&y0<spec.H?(y0*spec.W+x)*4:-1;
            var ib=y0+1>=0&&y0+1<spec.H?((y0+1)*spec.W+x)*4:-1;
            var alphaA=ia<0?0:source[ia+3];var alphaB=ib<0?0:source[ib+3];
            var alpha=alphaA*(1-blend)+alphaB*blend;
            for(var c=0;c<3;c++)
            {
                var a=ia<0?0:source[ia+c];var b=ib<0?0:source[ib+c];
                result[(y*spec.W+x)*4+c]=alpha==0?(byte)0:(byte)Math.Round((a*alphaA*(1-blend)+b*alphaB*blend)/alpha);
            }
            result[(y*spec.W+x)*4+3]=(byte)Math.Round(alpha);
        }
        return result;
    }
    private static byte[] Over(byte[] bottom,byte[] top)
    {
        for(var i=0;i<bottom.Length;i+=4)
        {
            var a=top[i+3]/255d;var b=bottom[i+3]/255d;var total=a+b*(1-a);
            for(var c=0;c<3;c++)bottom[i+c]=total==0?(byte)0:(byte)Math.Round((top[i+c]*a+bottom[i+c]*b*(1-a))/total);
            bottom[i+3]=(byte)Math.Round(total*255);
        }
        return bottom;
    }
    private static (byte[] Pixels,int Width,int Height) Load(string path)
    {
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();
        var bitmap=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
        return(bytes,bitmap.PixelWidth,bitmap.PixelHeight);
    }
    private static void Save(byte[] bytes,Spec spec,string file,string output)
    {
        var bitmap=BitmapSource.Create(spec.W,spec.H,96,96,PixelFormats.Bgra32,null,bytes,spec.W*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(output,file));encoder.Save(stream);
    }
    private sealed record Spec(string Name,int X,int Y,int W,int H,int Start,int End);
}
