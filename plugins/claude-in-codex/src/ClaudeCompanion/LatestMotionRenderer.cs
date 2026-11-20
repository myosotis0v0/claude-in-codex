using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal sealed class RenderedMotion : IDisposable
{
    internal MotionGeometry Geometry {get;}
    internal int Stride {get;}
    internal byte[] Pixels {get;}
    internal RenderedMotion(MotionGeometry geometry,SKBitmap bitmap)
    {
        Geometry=geometry;Stride=bitmap.RowBytes;
        Pixels=ArrayPool<byte>.Shared.Rent(bitmap.ByteCount);
        Marshal.Copy(bitmap.GetPixels(),Pixels,0,bitmap.ByteCount);
    }
    public void Dispose()=>ArrayPool<byte>.Shared.Return(Pixels);
}

internal sealed class LatestMotionRenderer : IDisposable
{
    private readonly object gate=new();
    private readonly Channel<(int Epoch,MotionFrame Frame)> requests=Channel.CreateBounded<(int,MotionFrame)>(
        new BoundedChannelOptions(1){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.DropOldest});
    private readonly Dispatcher dispatcher;
    private readonly Action changed;
    private int epoch;
    private bool closed,notificationPending;
    private RenderedMotion? ready;
    private Exception? failure;
    internal Task Completion {get;}

    internal LatestMotionRenderer(CharacterRig profile,Dispatcher dispatcher,Action changed,Action<MotionFrame>? beforeDraw=null)
    {
        this.dispatcher=dispatcher;this.changed=changed;
        Completion=Task.Run(()=>Run(profile,beforeDraw));
    }
    internal void Submit(MotionFrame frame)
    {
        lock(gate)if(!closed&&failure==null)requests.Writer.TryWrite((epoch,frame));
    }
    internal RenderedMotion? TakeLatest()
    {
        lock(gate){var frame=ready;ready=null;return frame;}
    }
    internal Exception? GetFailure(){lock(gate)return failure;}
    internal void Discard()
    {
        lock(gate){epoch++;ready?.Dispose();ready=null;}
    }
    private async Task Run(CharacterRig profile,Action<MotionFrame>? beforeDraw)
    {
        try
        {
            using var raster=new MotionRaster(profile:profile);
            raster.Renderer.PrepareMenuHair();
            while(await requests.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                if(!requests.Reader.TryRead(out var next))continue;
                lock(gate)if(closed||next.Epoch!=epoch)continue;
                beforeDraw?.Invoke(next.Frame);
                var rendered=new RenderedMotion(next.Frame.Geometry,raster.Draw(next.Frame));
                lock(gate)
                {
                    if(closed||next.Epoch!=epoch){rendered.Dispose();continue;}
                    ready?.Dispose();ready=rendered;
                    Notify();
                }
            }
        }
        catch(Exception error)
        {
            lock(gate)
            {
                if(closed)return;
                failure=error;requests.Writer.TryComplete();Notify();
            }
        }
    }
    private void Notify()
    {
        // Called under gate. Post at most one notification, below WPF input priority.
        if(notificationPending)return;
        notificationPending=true;
        _=dispatcher.InvokeAsync(()=>{
            lock(gate){notificationPending=false;if(closed)return;}
            changed();
        },DispatcherPriority.Background);
    }
    public void Dispose()
    {
        lock(gate)
        {
            closed=true;requests.Writer.TryComplete();
            ready?.Dispose();ready=null;
        }
    }
}

// WPF and the hit-test buffer are owned by the dispatcher, never by the raster worker.
internal sealed class MotionPresenter : IDisposable
{
    private WriteableBitmap? image;
    private RenderedMotion? pending,displayed;
    internal int DisplayedFrames {get;private set;}
    internal void Offer(RenderedMotion? next)
    {
        if(next==null)return;
        pending?.Dispose();pending=next;
    }
    internal WriteableBitmap? Present(MotionGeometry geometry)
    {
        if(pending is {} next)
        {
            if(next.Geometry!=geometry){next.Dispose();pending=null;}
            else
            {
                var (width,height,_,_,_)=geometry;
                var target=image?.PixelWidth==width&&image.PixelHeight==height?image:
                    new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
                if(target.TryLock(new Duration(TimeSpan.Zero)))
                {
                    try
                    {
                        var stride=target.BackBufferStride;
                        if(stride==next.Stride)Marshal.Copy(next.Pixels,0,target.BackBuffer,stride*height);
                        else for(int y=0;y<height;y++)
                            Marshal.Copy(next.Pixels,y*next.Stride,target.BackBuffer+y*stride,width*4);
                        target.AddDirtyRect(new Int32Rect(0,0,width,height));
                    }
                    finally{target.Unlock();}
                    image=target;displayed?.Dispose();displayed=next;pending=null;DisplayedFrames++;
                }
            }
        }
        return displayed==null?null:image;
    }
    internal Rect Bounds(MotionGeometry geometry,Rect target)
    {
        if(displayed is not {} frame||frame.Geometry==geometry)return target;
        // During a resize, keep the last image anchored until a fresh raster arrives.
        var old=frame.Geometry;var scale=geometry.ArtworkPixels/old.ArtworkPixels;
        var dipX=target.Width/geometry.Width;var dipY=target.Height/geometry.Height;
        return new(target.X+(geometry.InsetX-old.InsetX*scale)*dipX,target.Y+(geometry.InsetY-old.InsetY*scale)*dipY,
            old.Width*scale*dipX,old.Height*scale*dipY);
    }
    internal bool Hit(Point point,MotionGeometry geometry,Rect target)
    {
        if(displayed is not {} frame)return false;
        var rect=Bounds(geometry,target);
        if(!rect.Contains(point)||rect.Width<=0||rect.Height<=0)return false;
        var x=(int)((point.X-rect.X)*frame.Geometry.Width/rect.Width);
        var y=(int)((point.Y-rect.Y)*frame.Geometry.Height/rect.Height);
        return x>=0&&y>=0&&x<frame.Geometry.Width&&y<frame.Geometry.Height&&frame.Pixels[y*frame.Stride+x*4+3]>32;
    }
    internal void Discard()
    {
        pending?.Dispose();displayed?.Dispose();pending=displayed=null;
    }
    public void Dispose()=>Discard();
}
