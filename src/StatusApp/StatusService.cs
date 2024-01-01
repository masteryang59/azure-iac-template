using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace StatusApp;

internal enum CheckState { Pass, Warn, Fail }

internal sealed record Check(string Name, CheckState State, string Detail);

internal sealed record DeploymentInfo(
    string AppName, string Environment, string Version, string Slot, string InstanceId, string Region, string Host);

internal sealed record StatusReport(DeploymentInfo Deployment, IReadOnlyList<Check> Checks, VisitSummary? Visits)
{
    public CheckState Overall => Checks.Count == 0 ? CheckState.Pass : Checks.Max(c => c.State);
}

/// <summary>
/// Answers "did the deployment wire everything up?": can the app reach SQL as its managed identity,
/// did App Service resolve the Key Vault reference, is telemetry configured.
/// </summary>
internal sealed class StatusService(IConfiguration config, VisitStore visits, ILogger<StatusService> logger)
{
    internal const string KeyVaultReferencePrefix = "@Microsoft.KeyVault(";

    public static string AppVersion { get; } =
        typeof(StatusService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public DeploymentInfo Deployment(HttpContext? http = null) => new(
        AppName: config["App:Name"] ?? "iacdemo",
        Environment: config["App:EnvironmentName"] ?? "local",
        Version: AppVersion,
        Slot: config["Deployment:Slot"] ?? "local",
        // Both are set by App Service.
        InstanceId: Short(config["WEBSITE_INSTANCE_ID"]) ?? "local",
        Region: config["REGION_NAME"] ?? "local",
        Host: http?.Request.Host.Value ?? config["WEBSITE_HOSTNAME"] ?? "localhost");

    public async Task<StatusReport> GetReportAsync(HttpContext? http, bool recordVisit, CancellationToken ct)
    {
        var deployment = Deployment(http);
        var checks = new List<Check>();

        var (sqlCheck, sqlOk) = await CheckSqlAsync(ct);
        checks.Add(sqlCheck);
        checks.Add(CheckKeyVaultReference(config["Demo:ApiKey"]));
        checks.Add(CheckIdentity(config["AZURE_CLIENT_ID"], deployment.Environment));
        checks.Add(CheckTelemetry(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]));

        VisitSummary? summary = null;
        if (sqlOk)
        {
            try
            {
                if (recordVisit)
                {
                    await visits.RecordAsync(new Visit(DateTime.UtcNow, deployment.InstanceId, deployment.Slot, deployment.Version), ct);
                }
                summary = await visits.GetSummaryAsync(5, ct);
            }
            catch (SqlException ex)
            {
                logger.LogWarning(ex, "Visit table read/write failed");
                checks.Add(new Check("SQL read/write", CheckState.Fail, Describe(ex)));
            }
        }

        return new StatusReport(deployment, checks, summary);
    }

    private async Task<(Check Check, bool Ok)> CheckSqlAsync(CancellationToken ct)
    {
        if (!visits.IsConfigured)
        {
            return (new Check("Azure SQL", CheckState.Fail, "Sql:ConnectionString is not set"), false);
        }

        var watch = Stopwatch.StartNew();
        try
        {
            await using var connection = await visits.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT USER_NAME(), DB_NAME()", connection);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var detail = $"{reader.GetString(1)} as {reader.GetString(0)} in {watch.ElapsedMilliseconds} ms";
            return (new Check("Azure SQL", CheckState.Pass, detail), true);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "SQL check failed");
            return (new Check("Azure SQL", CheckState.Fail, Describe(ex)), false);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. a managed-identity token request failing outside Azure.
            logger.LogWarning(ex, "SQL check failed");
            return (new Check("Azure SQL", CheckState.Fail, ex.Message), false);
        }
    }

    internal static Check CheckKeyVaultReference(string? value)
    {
        const string name = "Key Vault reference";
        if (string.IsNullOrEmpty(value))
        {
            return new Check(name, CheckState.Warn, "Demo:ApiKey is not set");
        }

        // App Service leaves the raw reference in place when it can't resolve it
        // (missing secret, no role assignment, vault unreachable).
        if (value.StartsWith(KeyVaultReferencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new Check(name, CheckState.Warn, "unresolved: App Service could not read the secret");
        }

        return new Check(name, CheckState.Pass, $"Demo:ApiKey resolved ({value.Length} chars)");
    }

    internal static Check CheckIdentity(string? clientId, string environment)
    {
        if (!string.IsNullOrEmpty(clientId))
        {
            return new Check("Managed identity", CheckState.Pass, $"client ID {clientId}");
        }

        return environment == "local"
            ? new Check("Managed identity", CheckState.Pass, "not used locally")
            : new Check("Managed identity", CheckState.Warn, "AZURE_CLIENT_ID is not set");
    }

    internal static Check CheckTelemetry(string? connectionString) =>
        string.IsNullOrEmpty(connectionString)
            ? new Check("Application Insights", CheckState.Warn, "no connection string; telemetry off")
            : new Check("Application Insights", CheckState.Pass, "exporting traces and metrics");

    private static string Describe(SqlException ex) => $"SQL {ex.Number}: {FirstLine(ex.Message)}";

    private static string FirstLine(string s)
    {
        var i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s : s[..i];
    }

    private static string? Short(string? instanceId) =>
        string.IsNullOrEmpty(instanceId) ? null : instanceId[..Math.Min(12, instanceId.Length)];
}

    public static object ClassifyIntent(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object ClassifyIntent(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object ClassifyIntent(object data = null)
    {
        if (data == null) return null;
        return data;
    }