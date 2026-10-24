using System.Text.Json.Serialization;

namespace KultureCompass.Domain.Plans;

public enum PlanErrorCode
{
    [JsonStringEnumMemberName("qloo_unavailable")]
    QlooUnavailable,

    [JsonStringEnumMemberName("qloo_auth_failed")]
    QlooAuthFailed,

    [JsonStringEnumMemberName("qloo_no_evidence")]
    QlooNoEvidence,

    [JsonStringEnumMemberName("llm_unavailable")]
    LlmUnavailable,

    [JsonStringEnumMemberName("llm_quota_exceeded")]
    LlmQuotaExceeded,

    [JsonStringEnumMemberName("llm_output_invalid")]
    LlmOutputInvalid,

    [JsonStringEnumMemberName("timeout")]
    Timeout,

    [JsonStringEnumMemberName("internal_error")]
    InternalError,
}
