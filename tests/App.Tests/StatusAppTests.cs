using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StatusApp;

namespace App.Tests;

/// <summary>Hosts StatusApp in memory with the given settings layered over appsettings.json.</summary>
internal sealed class StatusAppFactory(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": keep appsettings.Development.json (LocalDB) out of the tests.
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public static StatusAppFactory With(string? sqlConnection, string? apiKey = "test-key", string? appName = null) => new(
        new Dictionary<string, string?>
        {
            ["Sql:ConnectionString"] = sqlConnection,
            ["Demo:ApiKey"] = apiKey,
            ["App:EnvironmentName"] = "test",
            ["App:Name"] = appName ?? "iacdemo",
            ["Deployment:Slot"] = "staging",
            ["AZURE_CLIENT_ID"] = "11111111-2222-3333-4444-555555555555",
        });
}

public sealed class StatusChecksTests
{
    [Theory]
    // CheckState is internal, so the expected state travels as its name.
    [InlineData(null, "Warn", "not set")]
    [InlineData("", "Warn", "not set")]
    [InlineData("@Microsoft.KeyVault(VaultName=kv;SecretName=demo-api-key)", "Warn", "unresolved")]
    [InlineData("@microsoft.keyvault(SecretUri=https://kv/secrets/x)", "Warn", "unresolved")]
    [InlineData("s3cr3t-value", "Pass", "resolved (12 chars)")]
    public void Key_vault_reference_states(string? value, string expected, string detail)
    {
        var check = StatusService.CheckKeyVaultReference(value);
        Assert.Equal(expected, check.State.ToString());
        Assert.Contains(detail, check.Detail);
        if (value is { Length: > 0 } && !value.StartsWith('@'))
        {
            Assert.DoesNotContain(value, check.Detail);
        }
    }

    [Fact]
    public void Missing_identity_only_warns_outside_local()
    {
        Assert.Equal(CheckState.Pass, StatusService.CheckIdentity(null, "local").State);
        Assert.Equal(CheckState.Warn, StatusService.CheckIdentity(null, "dev").State);
        Assert.Equal(CheckState.Pass, StatusService.CheckIdentity("abc", "dev").State);
    }

    [Fact]
    public void Overall_is_the_worst_check()
    {
        var d = new DeploymentInfo("a", "b", "c", "d", "e", "f", "g");
        Assert.Equal(CheckState.Pass, new StatusReport(d, [new("x", CheckState.Pass, "")], null).Overall);
        Assert.Equal(CheckState.Warn, new StatusReport(d, [new("x", CheckState.Pass, ""), new("y", CheckState.Warn, "")], null).Overall);
        Assert.Equal(CheckState.Fail, new StatusReport(d, [new("x", CheckState.Fail, ""), new("y", CheckState.Warn, "")], null).Overall);
    }
}

public sealed class StatusAppWithoutSqlTests
{
    // Nothing listens on port 1, so the connection is refused quickly.
    private const string Unreachable = "Server=tcp:127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=2;TrustServerCertificate=true";

    [Fact]
    public async Task Healthz_is_503_when_sql_is_unreachable()
    {
        await using var factory = StatusAppFactory.With(Unreachable);
        var response = await factory.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(StatusService.AppVersion, json.RootElement.GetProperty("version").GetString());
        var sql = json.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Azure SQL");
        Assert.Equal("fail", sql.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Healthz_is_503_when_no_connection_string()
    {
        await using var factory = StatusAppFactory.With(null);
        var response = await factory.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Sql:ConnectionString is not set", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Page_still_renders_with_failures_and_sends_security_headers()
    {
        await using var factory = StatusAppFactory.With(Unreachable, appName: "<script>alert(1)</script>");
        var response = await factory.CreateClient().GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("FAIL", html);
        Assert.DoesNotContain("dbo.Visits", html);
    }
}

[Collection("sql")]
public sealed class StatusAppWithSqlTests(SqlFixture sql)
{
    [Fact]
    public async Task Page_records_a_visit_and_lists_it()
    {
        sql.SkipIfUnavailable();
        await using var factory = StatusAppFactory.With(sql.ConnectionString);
        var client = factory.CreateClient();
        var before = await sql.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Visits");

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, await sql.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Visits"));
        Assert.Contains("rows in dbo.Visits", html);
        Assert.Contains("<td>staging</td>", html);
        Assert.Contains("Demo:ApiKey resolved", html);
    }

    [Fact]
    public async Task Healthz_does_not_write_and_reports_degraded_without_telemetry()
    {
        sql.SkipIfUnavailable();
        await using var factory = StatusAppFactory.With(sql.ConnectionString);
        var before = await sql.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Visits");

        var response = await factory.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("staging", json.RootElement.GetProperty("slot").GetString());
        var states = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("state").GetString());
        Assert.Equal("pass", states["Azure SQL"]);
        Assert.Equal("pass", states["Key Vault reference"]);
        Assert.Equal("warn", states["Application Insights"]);
        Assert.Equal(before, await sql.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Visits"));
    }

    [Fact]
    public async Task Unresolved_key_vault_reference_is_degraded_not_down()
    {
        sql.SkipIfUnavailable();
        await using var factory = StatusAppFactory.With(sql.ConnectionString, apiKey: "@Microsoft.KeyVault(VaultName=kv-x;SecretName=demo-api-key)");

        var response = await factory.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("unresolved", body);
    }
}

    public static object PingService(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object PingService(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object PingService(object data = null)
    {
        if (data == null) return null;
        return data;
    }