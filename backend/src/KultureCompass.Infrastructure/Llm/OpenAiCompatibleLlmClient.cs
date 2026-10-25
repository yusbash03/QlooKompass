using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Llm;

/// <summary>Fallback provider for any OpenAI-compatible <c>/chat/completions</c> API (Groq, OpenRouter, OpenAI) —
/// BE-04 §2. Tries <c>response_format: json_schema (strict)</c>; on a 400 falls back once to <c>json_object</c> and
/// remembers that per schema name. UNVERIFIED against a live provider (no key configured); covered by fake-handler
/// tests only.</summary>
public sealed class OpenAiCompatibleLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiCompatibleLlmClient> logger) : ILlmClient
{
    private static readonly ConcurrentDictionary<string, bool> JsonObjectOnly = new(StringComparer.Ordinal);

    public string ProviderName => "OpenAiCompatible";

    public string Model => options.Value.Model;

    public bool IsMock => false;

    public async Task<LlmResult> GenerateJsonAsync(LlmJsonRequest request, CancellationToken ct)
    {
        var o = options.Value;
        var sender = new LlmHttpSender(http, logger, ProviderName, TimeSpan.FromSeconds(o.MaxRetryDelaySeconds));
        var url = $"{o.BaseUrl.TrimEnd('/')}/chat/completions";
        var jsonObjectOnly = JsonObjectOnly.ContainsKey(request.SchemaName);

        while (true)
        {
            var body = BuildBody(request, o.Model, jsonObjectOnly).ToJsonString();
            var (response, failure) = await sender.SendAsync(() =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
                return message;
            }, o.Model, IsQuota, ct).ConfigureAwait(false);

            if (failure is not null)
                return failure;

            if (response!.Status == HttpStatusCode.BadRequest && !jsonObjectOnly)
            {
                jsonObjectOnly = true;
                JsonObjectOnly[request.SchemaName] = true;
                continue;
            }

            return response.Status == HttpStatusCode.OK
                ? ParseSuccess(response.Body)
                : LlmHttpSender.MapError(response, ProviderName, IsQuota);
        }
    }

    internal static JsonObject BuildBody(LlmJsonRequest request, string model, bool jsonObjectOnly)
    {
        var schema = request.ResponseSchema.DeepClone().AsObject();
        schema.Remove("$schema");
        schema.Remove("$id");

        var system = jsonObjectOnly
            ? request.SystemPrompt + "\n\nReturn JSON matching this schema exactly:\n" + schema.ToJsonString()
            : request.SystemPrompt;

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = request.UserContent }),
            ["temperature"] = Math.Min(request.Temperature, 0.5),
            ["max_tokens"] = request.MaxOutputTokens,
            ["response_format"] = jsonObjectOnly
                ? new JsonObject { ["type"] = "json_object" }
                : new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = SafeName(request.SchemaName), ["schema"] = schema, ["strict"] = true },
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
            return LlmResult.Fail(LlmErrorKind.InvalidResponse, "Provider returned a non-JSON response.");
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return LlmResult.Fail(LlmErrorKind.InvalidResponse, "Provider returned no choices.");

        var choice = choices[0];
        var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
        if (finish == "length")
            return LlmResult.Fail(LlmErrorKind.Truncated, "Provider output hit the token limit before finishing.");
        if (finish == "content_filter")
            return LlmResult.Fail(LlmErrorKind.Refused, "Provider filtered the output.");

        var message = choice.GetProperty("message");
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
            return LlmResult.Fail(LlmErrorKind.Refused, "Provider refused the request.");

        var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(content))
            return LlmResult.Fail(LlmErrorKind.InvalidResponse, "Provider returned an empty response.");

        LlmUsage? usage = null;
        if (root.TryGetProperty("usage", out var u))
        {
            usage = new LlmUsage(
                u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0,
                u.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0);
        }

        return LlmResult.Ok(content, usage);
    }

    internal static bool IsQuota(string body) =>
        body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("quota", StringComparison.OrdinalIgnoreCase) && body.Contains("exceeded", StringComparison.OrdinalIgnoreCase);

    private static string SafeName(string schemaName) =>
        new([.. schemaName.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')]);
}
