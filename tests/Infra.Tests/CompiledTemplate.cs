using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Infra.Tests;

/// <summary>One resource from the compiled template, with the (nested) template it lives in.</summary>
internal sealed record ArmResource(string Module, string Symbol, string Type, JsonObject Node, JsonObject Template)
{
    public bool Existing => Node["existing"]?.GetValue<bool>() == true;

    /// <summary>properties, with a whole-object "[variables('x')]" reference resolved.</summary>
    public JsonObject? Properties => Resolve(Node["properties"]) as JsonObject;

    public JsonNode? Resolve(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            var m = Regex.Match(s, @"^\[variables\('([^']+)'\)\]$");
            if (m.Success)
            {
                return Template["variables"]?[m.Groups[1].Value];
            }
        }
        return node;
    }

    public override string ToString() => $"{Module}/{Symbol} ({Type})";
}

/// <summary>
/// infra/main.bicep compiled fresh with the Bicep CLI, flattened across module deployments.
/// Handles both resource layouts Bicep emits: arrays (languageVersion 1) and symbolic-name objects (2.0).
/// </summary>
public sealed class CompiledTemplate
{
    public CompiledTemplate()
    {
        var result = Bicep.Run(["build", "infra/main.bicep", "--stdout"]);
        Assert.True(result.ExitCode == 0, "bicep build failed:\n" + result.All);
        Text = result.StdOut;
        Root = JsonNode.Parse(Text)!.AsObject();
        var all = new List<ArmResource>();
        Walk("main", Root, all);
        Resources = all;
    }

    public string Text { get; }

    public JsonObject Root { get; }

    internal IReadOnlyList<ArmResource> Resources { get; }

    internal IEnumerable<ArmResource> OfType(string type) =>
        Resources.Where(r => !r.Existing && string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase));

    private static void Walk(string module, JsonObject template, List<ArmResource> into)
    {
        IEnumerable<(string, JsonObject)> items = template["resources"] switch
        {
            JsonArray a => a.Select((r, i) => (i.ToString(), r!.AsObject())),
            JsonObject o => o.Select(kv => (kv.Key, kv.Value!.AsObject())),
            _ => [],
        };

        foreach (var (symbol, node) in items)
        {
            var type = node["type"]!.GetValue<string>();
            into.Add(new ArmResource(module, symbol, type, node, template));
            if (type == "Microsoft.Resources/deployments" && node["properties"]?["template"] is JsonObject nested)
            {
                Walk(ModuleName(node) ?? symbol, nested, into);
            }
        }
    }

    // Module deployments are named with a literal in main.bicep ('keyvault', 'sql', ...).
    private static string? ModuleName(JsonObject node) =>
        node["name"] is JsonValue v && v.TryGetValue<string>(out var s) && !s.StartsWith('[') ? s : null;
}

    public static object TraceRequest(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object TraceRequest(object data = null)
    {
        if (data == null) return null;
        return data;
    }