using System.Text.Json.Nodes;

namespace KultureCompass.Application.Abstractions;

/// <summary>The only way Application talks to an LLM (BE-04 §2). No tools, no internet, JSON-in-JSON-out. Output is
/// untrusted: callers always validate it (<see cref="Generation.OutputValidator"/>).</summary>
public interface ILlmClient
{
    string ProviderName { get; }

    string Model { get; }

    bool IsMock { get; }

    Task<LlmResult> GenerateJsonAsync(LlmJsonRequest request, CancellationToken ct);
}

/// <param name="SchemaName">Stable identifier for <paramref name="ResponseSchema"/> (providers may cache per-schema
/// capability decisions on it; the mock dispatches on it).</param>
public sealed record LlmJsonRequest(
    string SystemPrompt,
    string UserContent,
    JsonNode ResponseSchema,
    string SchemaName,
    int MaxOutputTokens,
    double Temperature = 0.4);

public sealed record LlmUsage(int PromptTokens, int OutputTokens, int? ThinkingTokens = null);

public enum LlmErrorKind
{
    QuotaExceeded,
    RateLimited,
    Unavailable,
    Timeout,
    Refused,
    Truncated,
    InvalidResponse,
    AuthFailed,
}

public abstract record LlmResult
{
    private LlmResult()
    {
    }

    public static LlmResult Ok(string json, LlmUsage? usage) => new Success(json, usage);

    public static LlmResult Fail(LlmErrorKind kind, string safeMessage) => new Failure(kind, safeMessage);

    public sealed record Success(string Json, LlmUsage? Usage) : LlmResult;

    public sealed record Failure(LlmErrorKind Kind, string SafeMessage) : LlmResult;
}
