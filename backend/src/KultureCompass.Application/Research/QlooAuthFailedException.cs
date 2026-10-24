namespace KultureCompass.Application.Research;

/// <summary>Thrown by <see cref="CulturalIntelligenceService"/> the moment any Qloo call fails with
/// <see cref="Qloo.QlooErrorKind.AuthFailed"/> (BE-03 §4: "AuthFailed -> abort plan (qloo_auth_failed)"). Unlike
/// Unavailable/Timeout/RateLimited, which become a limitation and research continues, an auth failure means every
/// subsequent call will fail the same way, so retrieval stops immediately. The future BE-6 pipeline catches this
/// and maps the plan to <c>PlanErrorCode.QlooAuthFailed</c>.</summary>
public sealed class QlooAuthFailedException(string message) : Exception(message);
