using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Llm;

/// <summary>Gemini <c>generateContent</c> with native structured output (<c>generationConfig.responseJsonSchema</c>).
/// Live-verified 2026-10-25 (docs/backend/llm-verified-contract.md): the full growth-plan schema is rejected with a
/// generic 400 for overall constraint complexity, while the same schema without array <c>minItems</c>/<c>maxItems</c>
/// is accepted — so on a 400 we retry once with array bounds stripped and remember that per schema name.
/// OutputValidator enforces the counts server-side either way.</summary>
public sealed class GeminiLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<GeminiLlmClient> logger) : ILlmClient
{
    private static readonly ConcurrentDictionary<string, bool> SimplifiedSchemas = new(StringComparer.Ordinal);

    private static readonly HashSet<string> RefusalReasons = new(StringComparer.Ordinal)
    {
        "SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII", "LANGUAGE",
    };

    public string ProviderName => "Gemini";

    public string Model => options.Value.Model;

    public bool IsMock => false;

    public async Task<LlmResult> GenerateJsonAsync(LlmJsonRequest request, CancellationToken ct)
    {
        var o = options.Value;
        var sender = new LlmHttpSender(http, logger, ProviderName, TimeSpan.FromSeconds(o.MaxRetryDelaySeconds));
        var url = $"{o.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(o.Model)}:generateContent";
        var simplified = SimplifiedSchemas.ContainsKey(request.SchemaName);

        while (true)
        {
            var body = BuildBody(request, simplified).ToJsonString();
            var (response, failure) = await sender.SendAsync(() =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                message.Headers.Add("x-goog-api-key", o.ApiKey); // header only — never in the query string
                return message;
            }, o.Model, IsDailyQuota, ct).ConfigureAwait(false);

            if (failure is not null)
                return failure;

            if (response!.Status == HttpStatusCode.BadRequest && !simplified)
            {
                simplified = true;
                SimplifiedSchemas[request.SchemaName] = true;
                continue;
            }

            return response.Status == HttpStatusCode.OK
                ? ParseSuccess(response.Body)
                : LlmHttpSender.MapError(response, ProviderName, IsDailyQuota);
        }
    }

    internal static JsonObject BuildBody(LlmJsonRequest request, bool simplifiedSchema)
    {
        var schema = request.ResponseSchema.DeepClone().AsObject();
        schema.Remove("$schema");
        schema.Remove("$id");
        if (simplifiedSchema)
            StripArrayBounds(schema);

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.UserContent }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = schema,
                ["maxOutputTokens"] = request.MaxOutputTokens,
                ["temperature"] = Math.Min(request.Temperature, 0.5), // BE-04 §5: temperature ≤ 0.5
            },
        };
    }

    internal static LlmResult ParseSuccess(string body)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            return LlmResult.Fail(LlmErrorKind.InvalidResponse, "Gemini returned a non-JSON response.");
        }

        var usage = ParseUsage(root);
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _)
                ? LlmResult.Fail(LlmErrorKind.Refused, "Gemini blocked the prompt.")
                : LlmResult.Fail(LlmErrorKind.InvalidResponse, "Gemini returned no candidates.");
        }

        var candidate = candidates[0];
        var finishReason = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
        if (finishReason == "MAX_TOKENS")
            return LlmResult.Fail(LlmErrorKind.Truncated, "Gemini output hit the token limit before finishing.");
        if (finishReason is not null && RefusalReasons.Contains(finishReason))
            return LlmResult.Fail(LlmErrorKind.Refused, $"Gemini stopped generating ({finishReason}).");

        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                var isThought = part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;
                if (!isThought && part.TryGetProperty("text", out var t))
                    text.Append(t.GetString());
            }
        }

        return text.Length == 0
            ? LlmResult.Fail(LlmErrorKind.InvalidResponse, "Gemini returned an empty response.")
            : LlmResult.Ok(text.ToString(), usage);
    }

    private static LlmUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var u))
            return null;
        static int Read(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
        var thoughts = Read(u, "thoughtsTokenCount");
        return new LlmUsage(Read(u, "promptTokenCount"), Read(u, "candidatesTokenCount"), thoughts == 0 ? null : thoughts);
    }

    /// <summary>Gemini returns RESOURCE_EXHAUSTED for both per-minute and per-day limits; only a per-day quota id means
    /// retrying today is pointless.</summary>
    internal static bool IsDailyQuota(string body) =>
        body.Contains("PerDay", StringComparison.OrdinalIgnoreCase) || body.Contains("per day", StringComparison.OrdinalIgnoreCase);

    private static void StripArrayBounds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("minItems");
                obj.Remove("maxItems");
                foreach (var (_, child) in obj.ToList())
                    StripArrayBounds(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    StripArrayBounds(child);
                break;
        }
    }
}
