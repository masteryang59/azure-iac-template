using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Infra.Tests;

/// <summary>
/// Invariants of the compiled template that hold for every environment. Parameter-dependent
/// settings are covered by ParameterFileTests and by PSRule (ps-rule.yaml), which expands the
/// param files.
/// </summary>
public sealed class TemplateTests(CompiledTemplate t) : IClassFixture<CompiledTemplate>
{
    [Fact]
    public void Lint_is_clean()
    {
        var result = Bicep.Run(["lint", "infra/main.bicep"]);
        Assert.True(result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.All), result.All);
    }

    [Fact]
    public void Committed_main_json_matches_a_fresh_build()
    {
        var committed = JsonNode.Parse(Repo.Read("infra", "main.json"))!.AsObject();
        var fresh = JsonNode.Parse(t.Text)!.AsObject();
        // The generator stamp holds the Bicep version and a hash; everything else must be identical.
        committed["metadata"]!.AsObject().Remove("_generator");
        fresh["metadata"]!.AsObject().Remove("_generator");

        Assert.True(JsonNode.DeepEquals(committed, fresh),
            "infra/main.json is stale. Run: bicep build infra/main.bicep --outfile infra/main.json");
    }

    [Fact]
    public void Web_app_and_slot_are_locked_down()
    {
        var sites = t.OfType("Microsoft.Web/sites").Concat(t.OfType("Microsoft.Web/sites/slots")).ToList();
        Assert.Equal(2, sites.Count);

        foreach (var site in sites)
        {
            var p = site.Properties!;
            Assert.True(p["httpsOnly"]!.GetValue<bool>(), $"{site}: httpsOnly");
            Assert.False(p["clientAffinityEnabled"]!.GetValue<bool>(), $"{site}: ARR affinity");
            Assert.NotNull(p["keyVaultReferenceIdentity"]);

            var cfg = p["siteConfig"]!;
            Assert.Equal("Disabled", Str(cfg["ftpsState"]));
            Assert.Equal("1.2", Str(cfg["minTlsVersion"]));
            Assert.Equal("1.2", Str(cfg["scmMinTlsVersion"]));
            Assert.False(cfg["remoteDebuggingEnabled"]!.GetValue<bool>());
            Assert.Equal("/healthz", Str(cfg["healthCheckPath"]));

            Assert.Equal("UserAssigned", Str(site.Node["identity"]!["type"]));
        }
    }

    [Fact]
    public void Basic_auth_publishing_is_off_for_ftp_and_scm_on_app_and_slot()
    {
        var policies = t.OfType("Microsoft.Web/sites/basicPublishingCredentialsPolicies")
            .Concat(t.OfType("Microsoft.Web/sites/slots/basicPublishingCredentialsPolicies"))
            .ToList();

        Assert.Equal(4, policies.Count);
        Assert.All(policies, p => Assert.False(p.Properties!["allow"]!.GetValue<bool>(), p.ToString()));
    }

    [Fact]
    public void Key_vault_uses_rbac_with_soft_delete_and_no_access_policies()
    {
        var vault = Assert.Single(t.OfType("Microsoft.KeyVault/vaults"));
        var p = vault.Properties!;

        Assert.True(p["enableRbacAuthorization"]!.GetValue<bool>());
        Assert.True(p["enableSoftDelete"]!.GetValue<bool>());
        Assert.False(p["enabledForTemplateDeployment"]!.GetValue<bool>());
        Assert.Null(p["accessPolicies"]);
    }

    [Fact]
    public void Sql_is_entra_only_with_tls_1_2_and_no_password_anywhere()
    {
        var server = Assert.Single(t.OfType("Microsoft.Sql/servers"));
        var p = server.Properties!;

        Assert.Equal("1.2", Str(p["minimalTlsVersion"]));
        Assert.True(p["administrators"]!["azureADOnlyAuthentication"]!.GetValue<bool>());
        Assert.Equal("ActiveDirectory", Str(p["administrators"]!["administratorType"]));
        Assert.DoesNotContain("administratorLoginPassword", t.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", t.Text, StringComparison.OrdinalIgnoreCase);

        var auditing = Assert.Single(t.OfType("Microsoft.Sql/servers/auditingSettings"));
        Assert.Equal("Enabled", Str(auditing.Properties!["state"]));
    }

    [Fact]
    public void App_connects_to_sql_as_its_managed_identity()
    {
        // The connection string is built in main.bicep; it must use the identity, never a secret.
        Assert.Contains("Authentication=Active Directory Managed Identity", t.Text);
        Assert.Matches(@"User Id=\{\d\}", t.Text);
    }

    [Fact]
    public void Every_role_the_template_assigns_is_allowed_by_bootstrap()
    {
        var bootstrap = Repo.Read("scripts", "bootstrap.sh");
        var allowedLine = Regex.Match(bootstrap, "ALLOWED_ROLE_IDS=\"([^\"]+)\"").Groups[1].Value;
        var allowed = Regex.Matches(allowedLine, "[0-9a-f-]{36}").Select(m => m.Value).ToHashSet();
        Assert.NotEmpty(allowed);

        var assignments = t.OfType("Microsoft.Authorization/roleAssignments").ToList();
        Assert.NotEmpty(assignments);
        foreach (var a in assignments)
        {
            // roleDefinitionId is an expression; the role GUID is a literal or a variable it references.
            var expr = Str(a.Properties!["roleDefinitionId"]);
            var guid = Regex.Match(expr, "[0-9a-f-]{36}").Value;
            if (guid.Length == 0)
            {
                var variable = Regex.Match(expr, @"variables\('([^']+)'\)").Groups[1].Value;
                guid = Str(a.Template["variables"]![variable]);
            }
            Assert.True(allowed.Contains(guid), $"{a} assigns role {guid}, which scripts/bootstrap.sh does not allow the pipeline to grant");
            Assert.Equal("ServicePrincipal", Str(a.Properties!["principalType"]));
        }
    }

    [Theory]
    [InlineData("Microsoft.Web/sites")]
    [InlineData("Microsoft.KeyVault/vaults")]
    [InlineData("Microsoft.Sql/servers/databases")]
    public void Diagnostics_go_to_log_analytics(string type)
    {
        var target = Assert.Single(t.OfType(type));
        var diagnostics = t.OfType("Microsoft.Insights/diagnosticSettings")
            .Where(d => d.Template == target.Template && Str(d.Node["scope"]).Contains($"'{type}'", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.NotNull(d.Properties!["workspaceId"]));
    }

    [Fact]
    public void Every_located_resource_is_tagged_and_location_is_never_hardcoded()
    {
        var located = t.Resources.Where(r => !r.Existing && r.Node["location"] is not null).ToList();
        Assert.NotEmpty(located);

        foreach (var r in located)
        {
            Assert.True(r.Node["tags"] is not null, $"{r} has no tags");
            var location = Str(r.Node["location"]);
            Assert.True(location.StartsWith('[') || location == "global", $"{r} hardcodes location '{location}'");
        }
    }

    [Fact]
    public void Secret_values_stay_secure_through_the_module_boundary()
    {
        Assert.Equal("secureObject", Str(t.Root["parameters"]!["secretValues"]!["type"]));

        var kvModule = t.Resources.Single(r => r.Type == "Microsoft.Resources/deployments" && r.Symbol == "keyVault");
        Assert.Equal("secureObject", Str(kvModule.Node["properties"]!["template"]!["parameters"]!["secretValues"]!["type"]));

        var outputs = t.Root["outputs"]!.AsObject().Select(o => o.Value!.ToJsonString());
        Assert.DoesNotContain(outputs, o => o.Contains("secretValues", StringComparison.Ordinal));
    }

    [Fact]
    public void Outputs_cover_what_the_pipeline_reads()
    {
        var deployStage = Repo.Read("pipelines", "templates", "deploy-stage.yml");
        var read = Regex.Matches(deployStage, @"Infra\.outputs\.(\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        var declared = t.Root["outputs"]!.AsObject().Select(o => o.Key).ToHashSet();

        Assert.NotEmpty(read);
        Assert.Subset(declared, read);
    }

    private static string Str(JsonNode? n) => n?.GetValue<string>() ?? "";
}

    public static object DestroySession(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object DestroySession(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object DestroySession(object data = null)
    {
        if (data == null) return null;
        return data;
    }