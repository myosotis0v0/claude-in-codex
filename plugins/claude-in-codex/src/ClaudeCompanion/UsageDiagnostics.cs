using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MotionLab;
using SkiaSharp;

namespace ClaudeCompanion;

internal static class UsageDiagnostics
{
    internal static async Task CheckConcurrentReads(Action<bool,string> check)
    {
        using var client=new UsageClient(()=>{
            var info=new ProcessStartInfo(Environment.ProcessPath!);
            info.ArgumentList.Add("--usage-test-fixture");return info;
        });
        var staleQuotaWasShown=false;
        client.Changed+=snapshot=>staleQuotaWasShown|=snapshot.ResetCredits?.AvailableCount==99;
        var titles=client.RefreshThreadTitlesAsync(new HashSet<string>{TaskEvents.HashIdentifier("fixture")});
        var quota=client.RefreshAsync();
        await Task.WhenAll(titles,quota).WaitAsync(TimeSpan.FromSeconds(5));
        check(client.Current.Windows.SingleOrDefault()?.UsedPercent==42&&client.Current.ResetCredits?.AvailableCount==1,
            "a queued thread-name lookup cannot skip or block the first quota read");
        check(!staleQuotaWasShown,"an account notification during the first read retries without showing the old snapshot");
    }

    internal static int RunFixture()
    {
        var input=new StreamReader(Console.OpenStandardInput());
        var output=new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true};
        void Reply(int id,object result)=>output.WriteLine(JsonSerializer.Serialize(new{id,result}));
        int? titles=null,quota=null;
        bool firstReadCompleted=false;
        for(string? line;(line=input.ReadLine())!=null;)
        {
            using var json=JsonDocument.Parse(line);var request=json.RootElement;
            if(!request.TryGetProperty("id",out var id))continue;
            switch(request.GetProperty("method").GetString())
            {
                case "initialize":Reply(id.GetInt32(),new{});break;
                case "thread/list":titles=id.GetInt32();break;
                case "account/rateLimits/read":
                    if(firstReadCompleted)
                        Reply(id.GetInt32(),new{rateLimits=new{primary=new{usedPercent=42,windowDurationMins=10080}},rateLimitResetCredits=new{availableCount=1}});
                    else quota=id.GetInt32();
                    break;
                default:return 1;
            }
            if(titles is {} t&&quota is {} q)
            {
                output.WriteLine("{\"method\":\"account/updated\",\"params\":{}}");
                Reply(q,new{rateLimitResetCredits=new{availableCount=99}});
                Reply(t,new{data=Array.Empty<object>(),nextCursor=(string?)null});
                firstReadCompleted=true;
                titles=quota=null;
            }
        }
        return 0;
    }

    internal static void CheckRibbon(Action<bool,string> check)
    {
        using var raster=new MotionRaster();
        var geometry=new MotionGeometry(222,208,192,18,16);
        var hands=new DeskHands{Enabled=true}.Snapshot();
        foreach(var (pose,touch,name) in new (Pose,HeadTouchPose?,string)[]{
            (new Pose(),null,"idle"),
            (new Pose{MenuPitchWeight=1},null,"menu"),
            (new Pose{HeadAngle=2,HeadX=3},new HeadTouchPose(true,0,0,0,default,5,default),"rub")})
        {
            var frame=new MotionFrame(geometry,pose,0,0,default,touch,hands);
            var normal=raster.Draw(frame).Pixels;
            var marked=raster.Draw(frame with{Pose=pose with{ResetRibbon=true}}).Pixels;
            var changed=0;var outside=0;
            for(int i=0;i<normal.Length;i++)if(normal[i]!=marked[i])
            {
                changed++;
                if(i%geometry.Width is <176 or >207||i/geometry.Width is <154 or >184)outside++;
            }
            check(changed is >=12 and <=200&&outside==0,$"{name}: reset artwork changes only the pinned ribbon tip ({changed} pixels)");
        }
    }
}
