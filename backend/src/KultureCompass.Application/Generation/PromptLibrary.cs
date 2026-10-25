using System.Reflection;
using System.Text.Json.Nodes;

namespace KultureCompass.Application.Generation;

/// <summary>Versioned prompts and response schemas, embedded at build time (BE-04 §3). The synthesis schema is the
/// contracts/growth-plan-llm-output.schema.json file itself — linked, not copied — so there is one source of truth.</summary>
public static class PromptLibrary
{
    // Declared first: the static properties below read it during type initialization (textual order).
    private static readonly Dictionary<string, string> Resources = LoadResources();

    /// <summary>Stored on every plan as <c>promptVersion</c>. Bump when any prompt text changes.</summary>
    public const string PromptVersion = "synth-v1";

    public const string SchemaVersion = "1.0";

    public const string InterpretationSchemaName = "brief-interpretation-v1";
    public const string GrowthPlanSchemaName = "growth-plan-llm-output-1.0";
    public const string LlmOnlySchemaName = "growth-plan-llm-only-1.0";
    public const string LlmOnlyPromptVersion = "llm-only-v1";

    public static string InterpretSystem { get; } = Load("Prompts.system.interpret-v1.md");
    public static string SynthSystem { get; } = Load("Prompts.system.synth-v1.md");
    public static string SynthUserTemplate { get; } = Load("Prompts.user.synth-v1.md");
    public static string RepairAddendum { get; } = Load("Prompts.system.repair-v1.md");
    public static string LlmOnlySystem { get; } = Load("Prompts.system.llm-only-v1.md");

    /// <summary>Fresh copy each call — callers (provider clients) may mutate it to adapt to provider limits.</summary>
    public static JsonNode InterpretationSchema() => ParseSchema("Schemas.brief-interpretation.schema.json");

    public static JsonNode GrowthPlanSchema() => ParseSchema("Schemas.growth-plan-llm-output.schema.json");

    /// <summary>BE-04 §4 LLM-only variant: the v1.0 schema with every evidence-key field removed, so a model with no
    /// evidence is not forced to invent <c>ev_###</c> keys, and mentionedEntities.evidenceKey is always null.</summary>
    public static JsonNode LlmOnlySchema()
    {
        var schema = GrowthPlanSchema().AsObject();
        var props = schema["properties"]!.AsObject();
        foreach (var section in new[] { "insights", "collaborations", "campaigns" })
        {
            var item = props[section]!["items"]!.AsObject();
            item["properties"]!.AsObject().Remove("evidenceKeys");
            item["properties"]!.AsObject().Remove("partnerEvidenceKey");
            var required = item["required"]!.AsArray();
            foreach (var node in required.Where(n => n!.GetValue<string>() is "evidenceKeys" or "partnerEvidenceKey").ToList())
                required.Remove(node);
        }
        props["mentionedEntities"]!["items"]!["properties"]!["evidenceKey"] = new JsonObject { ["type"] = "null" };
        return schema;
    }

    private static Dictionary<string, string> LoadResources()
    {
        var assembly = typeof(PromptLibrary).GetTypeInfo().Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Prompts.", StringComparison.Ordinal) || n.StartsWith("Schemas.", StringComparison.Ordinal))
            .ToDictionary(n => n, n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });
    }

    private static string Load(string name) =>
        Resources.TryGetValue(name, out var text)
            ? text
            : throw new InvalidOperationException($"Missing embedded prompt resource '{name}'.");

    private static JsonNode ParseSchema(string name) =>
        JsonNode.Parse(Load(name)) ?? throw new InvalidOperationException($"Schema resource '{name}' is empty.");
}
