namespace KultureCompass.Infrastructure.Identity;

/// Infrastructure-only persistence model (BE-02 §6). Service wiring (rotation, reuse detection) is BE-5.
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedByIpHash { get; set; }
    public string? UserAgent { get; set; }
}
