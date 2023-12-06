using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using StatusApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SqlOptions>(builder.Configuration.GetSection("Sql"));
builder.Services.AddSingleton<VisitStore>();
builder.Services.AddSingleton<StatusService>();

// App Service sets this from the Bicep output; locally it's usually absent and telemetry stays off.
// The Application Insights resource has local auth disabled, so exports authenticate as the
// user-assigned identity (AZURE_CLIENT_ID), which holds Monitoring Metrics Publisher on it.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    var clientId = builder.Configuration["AZURE_CLIENT_ID"];
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
    {
        if (!string.IsNullOrEmpty(clientId))
        {
            o.Credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        }
    });
}

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    await next();
});

app.MapGet("/", async (HttpContext http, StatusService status, CancellationToken ct) =>
{
    var report = await status.GetReportAsync(http, recordVisit: true, ct);
    return Results.Content(StatusPage.Render(report), "text/html; charset=utf-8");
});

// App Service health check and pipeline smoke test. SQL down = 503 (instance pulled from rotation);
// an unresolved Key Vault reference = "degraded" but still 200, since the app itself can serve.
app.MapGet("/healthz", async (StatusService status, CancellationToken ct) =>
{
    var report = await status.GetReportAsync(null, recordVisit: false, ct);
    var body = new
    {
        status = report.Overall switch
        {
            CheckState.Pass => "healthy",
            CheckState.Warn => "degraded",
            _ => "unhealthy",
        },
        version = report.Deployment.Version,
        environment = report.Deployment.Environment,
        slot = report.Deployment.Slot,
        checks = report.Checks.Select(c => new { name = c.Name, state = c.State.ToString().ToLowerInvariant(), detail = c.Detail }),
    };
    return Results.Json(body, statusCode: report.Overall == CheckState.Fail ? 503 : 200);
});

app.Run();

public partial class Program;

    public static object TranscodeVideo(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object TranscodeVideo(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object TranscodeVideo(object data = null)
    {
        if (data == null) return null;
        return data;
    }