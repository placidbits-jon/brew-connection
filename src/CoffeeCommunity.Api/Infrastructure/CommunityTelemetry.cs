using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CoffeeCommunity.Api.Infrastructure;

public sealed record ReplayRequest(string RunId);
public sealed record TelemetryPoint(int Second, double WaterGrams, double FlowRate, double TemperatureC);
public sealed record IngestRequest(TelemetryPoint[] Samples);
public sealed class CommunityTelemetry(ArcadeDbClient db, CommunityGraphGate gate, DemoReadiness readiness)
{
    const long Epoch = 1789401600000;
    readonly List<GraphQuery> queries = [];
    static string Str(JsonElement x,string key)=>x.TryGetProperty(key,out var v)?v.ToString():"";
    static double Num(JsonElement x,string key)=>x.GetProperty(key).GetDouble();
    static long Time(JsonElement x,string key) => x.GetProperty(key).ValueKind == JsonValueKind.Number ? x.GetProperty(key).GetInt64() : new DateTimeOffset(DateTime.Parse(Str(x,key),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)).ToUnixTimeMilliseconds();
    static void Key(string? value) { if(value is null || !Regex.IsMatch(value,"^[a-z0-9][a-z0-9-]{0,79}$")) throw new GraphRequestException(400,"Use 1–80 lowercase letters, digits or hyphens."); }
    public async Task<object> Run(Func<Task<object>> action,CancellationToken ct)
    { await gate.Semaphore.WaitAsync(ct);try { if(!readiness.SchemaReady) throw new GraphRequestException(503,"Demo data is not ready.");return await action(); } finally { gate.Semaphore.Release(); } }
    async Task<JsonElement[]> Query(string label,string sql,object? args,CancellationToken ct)
    { queries.Add(new(label,"sql",sql,args));using var r=await db.QueryAsync("sql",sql,args,ct);return r.RootElement.GetProperty("result").EnumerateArray().Select(x=>x.Clone()).ToArray(); }
    async Task<string> Plan(string sql,object? args,CancellationToken ct)
    { queries.Add(new("Native time-series query plan","sql","EXPLAIN "+sql,args));using var r=await db.QueryAsync("sql","EXPLAIN "+sql,args,ct);return r.RootElement.GetProperty("explain").GetString()??""; }
    async Task Command(string sql,object? args,CancellationToken ct)
    { queries.Add(new("Telemetry lifecycle","sql",sql,args));using var r=await db.CommandAsync("sql",sql,args,ct); }
    async Task EnsureRuns(CancellationToken ct)
    {
        await Command("CREATE DOCUMENT TYPE TelemetryRun IF NOT EXISTS",null,ct);
        await Command("CREATE PROPERTY TelemetryRun.slug IF NOT EXISTS STRING",null,ct);
        await Command("CREATE INDEX IF NOT EXISTS ON TelemetryRun (slug) UNIQUE_HASH",null,ct);
    }
    async Task<JsonElement> BrewRecord(string slug,CancellationToken ct)
    { Key(slug);var rows=await Query("Stable brew","SELECT slug,name,device,method,targetFlowRate FROM Brew WHERE slug=:slug",new{slug},ct);return rows.FirstOrDefault().ValueKind==JsonValueKind.Undefined?throw new GraphRequestException(404,"Brew not found."):rows[0]; }
    Task<JsonElement[]> Raw(string slug,string device,CancellationToken ct) =>
        Query("Native time-series tag and time range", "SELECT ts,water_grams,flow_rate,temperature_c FROM BrewTelemetry WHERE ts BETWEEN :from AND :to AND brew_id=:slug AND device=:device ORDER BY ts",new {from=Epoch,to=Epoch+59999,slug,device},ct);
    public async Task<object> Brew(string slug,string runId,CancellationToken ct)
    {
        Key(runId);var brew=await BrewRecord(slug,ct);string status="complete",device=Str(brew,"device");
        if(runId!="seed") { await EnsureRuns(ct);var run=await Query("Replay identity","SELECT slug,brewSlug,status FROM TelemetryRun WHERE slug=:runId",new{runId},ct);if(run.Length==0||Str(run[0],"brewSlug")!=slug)throw new GraphRequestException(404,"Replay not found for this brew.");status=Str(run[0],"status");device="replay-"+runId; }
        var raw=await Raw(slug,device,ct);
        var brewer=(await Query("Brewer from graph","SELECT @out.slug AS slug,@out.name AS name FROM BREWED WHERE @in.slug=:slug",new{slug},ct)).First();
        var recipe=(await Query("Recipe from graph","SELECT @in.slug AS slug,@in.name AS name FROM USED_RECIPE WHERE @out.slug=:slug",new{slug},ct)).First();
        var revision=(await Query("Pinned recipe revision","SELECT revision.slug AS slug,revision.revision AS revision,revision.steps AS steps FROM USED_RECIPE WHERE @out.slug=:slug",new{slug},ct)).First();
        var steps=revision.GetProperty("steps").EnumerateArray().OrderBy(x=>Num(x,"atSeconds")).ToArray();
        double Water(int second) {var left=steps.LastOrDefault(x=>Num(x,"atSeconds")<=second);var right=steps.FirstOrDefault(x=>Num(x,"atSeconds")>second);if(left.ValueKind==JsonValueKind.Undefined)return Num(steps[0],"waterGrams");if(right.ValueKind==JsonValueKind.Undefined)return Num(left,"waterGrams");return Num(left,"waterGrams")+(Num(right,"waterGrams")-Num(left,"waterGrams"))*(second-Num(left,"atSeconds"))/(Num(right,"atSeconds")-Num(left,"atSeconds"));}
        var target=Num(brew,"targetFlowRate");
        var samples=raw.OrderBy(x=>Time(x,"ts")).Select(x=>{var second=(int)((Time(x,"ts")-Epoch)/1000);return new {second,timestamp=(long)Time(x,"ts"),waterGrams=Num(x,"water_grams"),flowRate=Num(x,"flow_rate"),temperatureC=Num(x,"temperature_c"),targetWaterGrams=Water(second),targetFlowRate=target,deviation=Num(x,"flow_rate")-target,waterDeviation=Num(x,"water_grams")-Water(second)};}).ToArray();
        var anomaly=samples.Where(x=>x.deviation>0).OrderByDescending(x=>x.deviation).Select(x=>new{name=$"{x.second}-second pour spike",second=x.second,actual=x.flowRate,target=x.targetFlowRate}).FirstOrDefault();
        var indexPlan=await Plan("SELECT ts,water_grams,flow_rate FROM BrewTelemetry WHERE ts BETWEEN :from AND :to AND brew_id=:slug AND device=:device",new{from=Epoch,to=Epoch+59999,slug,device},ct);
        return new {indexPlan,brew,brewer,recipe,recipeRevision=revision,runId,status,sampleCount=samples.Length,samples,anomaly,targetDescription="Flow target is stored on the brew; water targets interpolate the pinned immutable recipe steps.",indexDescription="Native time-series columnar storage, indexed brew/device tags and a bounded 60-second range.",queries};
    }
    public async Task<object> Replay(string slug,ReplayRequest request,CancellationToken ct)
    {
        Key(request.RunId);if(request.RunId=="seed")throw new GraphRequestException(400,"Choose a replay identity other than seed.");await BrewRecord(slug,ct);await EnsureRuns(ct);
        var existing=await Query("Replay identity","SELECT brewSlug,status FROM TelemetryRun WHERE slug=:runId",new{runId=request.RunId},ct);
        if(existing.Length>0 && Str(existing[0],"brewSlug")!=slug)throw new GraphRequestException(409,"Run identity already belongs to another brew.");
        if(existing.Length==0)await Command("INSERT INTO TelemetryRun SET slug=:runId,brewSlug=:slug,status='queued'",new{runId=request.RunId,slug},ct);
        return new{runId=request.RunId,brewSlug=slug,status=existing.Length==0?"queued":Str(existing[0],"status"),queries};
    }
    public async Task<object> Pending(CancellationToken ct)
    { await EnsureRuns(ct);return new{runs=await Query("Pending simulator jobs","SELECT slug AS runId,brewSlug FROM TelemetryRun WHERE status='queued' LIMIT 10",null,ct)}; }
    public async Task<object> Ingest(string runId,IngestRequest request,CancellationToken ct)
    {
        Key(runId);if(request.Samples is null || request.Samples.Length is <1 or >60 || request.Samples.Any(x=>x is null) || request.Samples.Select(x=>x.Second).Distinct().Count()!=request.Samples.Length || request.Samples.Any(x=>x.Second<0||x.Second>59))throw new GraphRequestException(400,"Supply 1–60 distinct deterministic samples with seconds between 0 and 59.");
        await EnsureRuns(ct);var runs=await Query("Replay identity","SELECT brewSlug FROM TelemetryRun WHERE slug=:runId",new{runId},ct);if(runs.Length==0)throw new GraphRequestException(404,"Replay not found.");var slug=Str(runs[0],"brewSlug");
        if(request.Samples.Any(x=>x.WaterGrams!=x.Second*4 || x.FlowRate!=(slug=="blueberry-bloom-v60"&&x.Second==30?12:4) || Math.Abs(x.TemperatureC-(93-x.Second*.02))>.0001))throw new GraphRequestException(400,"Samples must match this demo's deterministic simulation.");
        var brewer=(await Query("Correlate brewer tag","SELECT @out.slug AS slug FROM BREWED WHERE @in.slug=:slug",new{slug},ct)).First();var existing=await Raw(slug,"replay-"+runId,ct);var timestamps=existing.Select(x=>(long)Time(x,"ts")).ToHashSet();var lines=new StringBuilder();
        foreach(var p in request.Samples.OrderBy(x=>x.Second))if(!timestamps.Contains(Epoch+p.Second*1000L))lines.Append(CultureInfo.InvariantCulture,$"BrewTelemetry,brew_id={slug},brewer_id={Str(brewer,"slug")},device=replay-{runId},method=v60 water_grams={p.WaterGrams:F1},flow_rate={p.FlowRate:F1},temperature_c={p.TemperatureC:F2} {Epoch+p.Second*1000L}\n");
        if(lines.Length>0)await db.WriteTimeSeriesAsync(lines.ToString(),ct);
        var sampleCount=(await Raw(slug,"replay-"+runId,ct)).Length;
        var status=sampleCount==60?"complete":"queued";
        await Command("UPDATE TelemetryRun SET status=:status WHERE slug=:runId",new{runId,status},ct);return new{runId,status,sampleCount};
    }
    public async Task<object> Pulse(int bucketMinutes,CancellationToken ct)
    {
        if(bucketMinutes is not(1 or 5 or 10 or 30 or 60))throw new GraphRequestException(400,"Bucket minutes must be 1, 5, 10, 30 or 60.");
        var args=new{from=Epoch,to=Epoch+7199999,eventId="brew-connection-2026"};
        var rows=await Query("Native time buckets",$"SELECT ts.timeBucket('{bucketMinutes}m',ts) AS timestamp,sum(count) AS count FROM EventActivity WHERE ts BETWEEN :from AND :to AND event_id=:eventId GROUP BY timestamp ORDER BY timestamp",args,ct);
        var aggregate=(await Query("Native percentile","SELECT count(*) AS sampleCount,sum(count) AS totalCount,ts.percentile(count,0.95) AS percentile95 FROM EventActivity WHERE ts BETWEEN :from AND :to AND event_id=:eventId",args,ct)).First();
        var downsampled=await Query("Native query-time downsampling","SELECT ts.timeBucket('30m',ts) AS timestamp,avg(count) AS averageCount,sum(count) AS count FROM EventActivity WHERE ts BETWEEN :from AND :to AND event_id=:eventId GROUP BY timestamp ORDER BY timestamp",args,ct);
        var rate=(await Query("Native water rate","SELECT ts.rate(water_grams,ts) AS waterGramsPerSecond FROM BrewTelemetry WHERE ts BETWEEN :from AND :to AND brew_id='blueberry-bloom-v60' AND device='scale-0'",new{from=Epoch,to=Epoch+59999},ct)).First();
        var eventRecord=(await Query("Event tag link","SELECT slug,name FROM Event WHERE slug=:eventId",args,ct)).First();var area=(await Query("Area tag link","SELECT slug,name FROM VenueArea WHERE slug='pour-over-bar'",null,ct)).First();
        var indexPlan=await Plan("SELECT ts,count FROM EventActivity WHERE ts BETWEEN :from AND :to AND event_id=:eventId",args,ct);
        return new { indexPlan,@event=eventRecord,area,bucketMinutes,buckets=rows.Select(x=>new{timestamp=Time(x,"timestamp"),count=Num(x,"count"),ratePerMinute=Num(x,"count")/bucketMinutes}),sampleCount=Num(aggregate,"sampleCount"),totalCount=Num(aggregate,"totalCount"),percentile95=Num(aggregate,"percentile95"),ratePerMinute=Num(aggregate,"totalCount")/120,waterGramsPerSecond=Num(rate,"waterGramsPerSecond"),downsampled=downsampled.Select(x=>new{timestamp=Time(x,"timestamp"),averageCount=Num(x,"averageCount"),count=Num(x,"count")}),retention=await RetentionStatus(ct),rateDescription="Event rate = native SUM(count) / 120-minute window (API division). Native ts.rate measures the seeded brew's water-weight slope.",downsamplingDescription="Native query-time 30-minute AVG/SUM; original samples are retained.",indexDescription="Native time-series storage with event tag and timestamp range; SQL time buckets and percentile.",queries};
    }
    async Task<object> RetentionStatus(CancellationToken ct)
    {
        var types=await Query("Retention example availability","SELECT name FROM schema:types WHERE name='DemoRetention'",null,ct);if(types.Length==0)return new{status="not-started",beforeCount=2,afterCount=0,retentionDays=1,detail="Start an isolated native retention example. Historical story data has no retention policy."};
        var rows=await Query("Native retention result","SELECT count(*) AS n FROM DemoRetention",null,ct);var count=(int)Num(rows[0],"n");return new{status=count<=1?"complete":"waiting",beforeCount=2,afterCount=count,retentionDays=1,detail="ArcadeDB's 60-second maintenance cycle removes the two-day-old sample; the recent sample remains. This sacrificial type is separate from authored telemetry."};
    }
    public async Task<object> Retention(CancellationToken ct)
    {
        var types=await Query("Retention example availability","SELECT name FROM schema:types WHERE name='DemoRetention'",null,ct);
        if(types.Length>0)await Command("DROP TIMESERIES TYPE DemoRetention",null,ct);
        await Command("CREATE TIMESERIES TYPE DemoRetention TIMESTAMP ts PRECISION MILLISECOND TAGS (run STRING) FIELDS (value DOUBLE) SHARDS 1 RETENTION 1 DAYS COMPACTION_INTERVAL 1 MINUTES",null,ct);
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();await db.WriteTimeSeriesAsync($"DemoRetention,run=retention value=1.0 {now-172800000}\nDemoRetention,run=retention value=2.0 {now}",ct);
        return new{retention=await RetentionStatus(ct),queries};
    }
}
public static class CommunityTelemetryEndpoints
{
    public static void MapCommunityTelemetry(this WebApplication app)
    {
        var g=app.MapGroup("/api/demo");g.AddEndpointFilter(async(c,next)=>{try{return await next(c);}catch(GraphRequestException e){return Results.Json(new{message=e.Message},statusCode:e.Status);}});
        g.MapGet("/brews/{slug}",(string slug,string? runId,CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Brew(slug,runId??"seed",ct),ct));
        g.MapPost("/brews/{slug}/replay",(string slug,ReplayRequest r,CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Replay(slug,r,ct),ct));
        g.MapGet("/telemetry/pending",(CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Pending(ct),ct));
        g.MapPost("/telemetry/runs/{runId}/ingest",(string runId,IngestRequest r,CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Ingest(runId,r,ct),ct));
        g.MapGet("/pulse",(int? bucketMinutes,CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Pulse(bucketMinutes??10,ct),ct));
        g.MapPost("/pulse/retention",(CommunityTelemetry t,CancellationToken ct)=>t.Run(()=>t.Retention(ct),ct));
    }
}
