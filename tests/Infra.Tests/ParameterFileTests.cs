using System.Text.Json.Nodes;

namespace Infra.Tests;

/// <summary>Builds infra/env/*.bicepparam with the Bicep CLI and checks per-environment intent.</summary>
public sealed class ParameterFileTests
{
    private const string AdminId = "11111111-2222-3333-4444-555555555555";

    private static JsonObject Build(string env, Dictionary<string, string?>? extraEnv = null)
    {
        var vars = new Dictionary<string, string?>
        {
            ["SQL_ADMIN_OBJECT_ID"] = AdminId,
            ["SQL_ADMIN_LOGIN"] = null,
            ["DEMO_API_KEY"] = null,
            ["ALERT_EMAIL"] = null,
            ["DEV_CLIENT_IP"] = null,
        };
        foreach (var (k, v) in extraEnv ?? [])
        {
            vars[k] = v;
        }

        var result = Bicep.Run(["build-params", $"infra/env/{env}.bicepparam", "--stdout"], vars);
        Assert.True(result.ExitCode == 0, result.All);
        var wrapper = JsonNode.Parse(result.StdOut)!;
        var parameters = JsonNode.Parse(wrapper["parametersJson"]!.GetValue<string>())!["parameters"]!.AsObject();
        return new JsonObject(parameters.Select(p => KeyValuePair.Create(p.Key, p.Value!["value"]?.DeepClone())));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("prod")]
    public void Param_file_targets_its_own_environment(string env)
    {
        var p = Build(env);
        Assert.Equal(env, p["environment"]!.GetValue<string>());
        Assert.Equal("iacdemo", p["appName"]!.GetValue<string>());
        Assert.Equal(AdminId, p["sqlAdmin"]!["objectId"]!.GetValue<string>());
        // Matches the service principal scripts/bootstrap.sh creates.
        Assert.Equal($"sp-iacdemo-{env}", p["sqlAdmin"]!["login"]!.GetValue<string>());
    }

    [Fact]
    public void Prod_is_hardened()
    {
        var p = Build("prod");

        Assert.True(p["keyVaultPurgeProtection"]!.GetValue<bool>());
        Assert.Equal(90, p["keyVaultSoftDeleteDays"]!.GetValue<int>());
        Assert.True(p["enablePrivateNetworking"]!.GetValue<bool>());
        Assert.True(p["enableStagingSlot"]!.GetValue<bool>());
        Assert.True(p["enableSqlDefender"]!.GetValue<bool>());
        Assert.StartsWith("P", p["appServicePlan"]!["skuName"]!.GetValue<string>());
        Assert.True(p["appServicePlan"]!["instanceCount"]!.GetValue<int>() >= 2);
        Assert.False(p["sqlDatabase"]!["useFreeLimit"]!.GetValue<bool>());
        Assert.Equal("Geo", p["sqlDatabase"]!["backupStorageRedundancy"]!.GetValue<string>());
        Assert.True(p["sqlDatabase"]!["pointInTimeRestoreDays"]!.GetValue<int>() >= 14);
    }

    [Fact]
    public void Dev_is_cheap_and_disposable()
    {
        var p = Build("dev");

        Assert.False(p["keyVaultPurgeProtection"]!.GetValue<bool>());
        Assert.False(p["enablePrivateNetworking"]!.GetValue<bool>());
        Assert.True(p["sqlDatabase"]!["useFreeLimit"]!.GetValue<bool>());
        Assert.StartsWith("GP_S_", p["sqlDatabase"]!["skuName"]!.GetValue<string>());
        Assert.Empty(p["sqlFirewallAllowedIps"]!.AsArray());
    }

    [Fact]
    public void Dev_client_ip_opens_the_firewall_only_when_set()
    {
        var p = Build("dev", new() { ["DEV_CLIENT_IP"] = "203.0.113.7" });
        Assert.Equal(["203.0.113.7"], p["sqlFirewallAllowedIps"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("prod")]
    public void Secret_value_comes_only_from_the_environment(string env)
    {
        Assert.Equal("", Build(env)["secretValues"]!["demo-api-key"]!.GetValue<string>());
        Assert.Equal("from-env", Build(env, new() { ["DEMO_API_KEY"] = "from-env" })["secretValues"]!["demo-api-key"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("prod")]
    public void Missing_sql_admin_fails_at_build_time(string env)
    {
        var result = Bicep.Run(["build-params", $"infra/env/{env}.bicepparam", "--stdout"],
            new Dictionary<string, string?> { ["SQL_ADMIN_OBJECT_ID"] = null });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SQL_ADMIN_OBJECT_ID", result.All);
    }

    [Fact]
    public void Every_secret_app_setting_has_a_value_slot()
    {
        foreach (var env in new[] { "dev", "prod" })
        {
            var p = Build(env);
            var names = p["keyVaultAppSettings"]!.AsArray().Select(s => s!["secretName"]!.GetValue<string>());
            var values = p["secretValues"]!.AsObject().Select(kv => kv.Key);
            Assert.Equal(names.Order(), values.Order());
        }
    }
}

    public static object MeasureLatency(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object MeasureLatency(object data = null)
    {
        if (data == null) return null;
        return data;
    }