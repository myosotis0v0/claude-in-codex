using System.Text.Json;

namespace ClaudeCompanion;

public sealed record ResetCredit(DateTimeOffset? ExpiresAt)
{
    internal bool IsExpiring(DateTimeOffset now,int days)=>ExpiresAt is {} expiry&&expiry>now&&expiry-now<=TimeSpan.FromDays(days);
}

public sealed record ResetCredits(int AvailableCount,IReadOnlyList<ResetCredit>? Details)
{
    internal const int DefaultReminderDays=7,MaximumReminderDays=30;

    internal int ExpiringCount(DateTimeOffset now,int days)=>Math.Min(AvailableCount,Details?.Count(c=>c.IsExpiring(now,days))??0);

    internal static ResetCredits? Parse(JsonElement result)
    {
        if(result.ValueKind!=JsonValueKind.Object||!result.TryGetProperty("rateLimitResetCredits",out var credits)||credits.ValueKind!=JsonValueKind.Object||
            !credits.TryGetProperty("availableCount",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var available)||available<0)return null;
        if(!credits.TryGetProperty("credits",out var details)||details.ValueKind!=JsonValueKind.Array)return new(available,null);

        var list=new List<ResetCredit>();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in details.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("status",out var status)||status.ValueKind!=JsonValueKind.String||status.GetString()!="available")continue;
            if(item.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String&&id.GetString() is {Length:>0} name&&!ids.Add(name))continue;
            list.Add(new(ReadDate(item,"expiresAt")));
        }
        return new(available,list.OrderBy(x=>x.ExpiresAt??DateTimeOffset.MaxValue).Take(available).ToArray());
    }

    internal static DateTimeOffset? ReadDate(JsonElement parent,string key)
    {
        if(parent.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var seconds))
            try{return DateTimeOffset.FromUnixTimeSeconds(seconds);}catch(ArgumentOutOfRangeException){}
        return null;
    }
}
