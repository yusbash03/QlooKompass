using System.Security.Cryptography;
using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KultureCompass.Infrastructure.Identity;

/// <summary>Opaque 64-byte refresh tokens; only the SHA-256 hex hash is stored. Every use rotates within the same
/// family; presenting an already-revoked token revokes the whole family (02-security §1).</summary>
public sealed class RefreshTokenStore(AppDbContext db, IOptions<JwtOptions> options, TimeProvider clock) : IRefreshTokenStore
{
    public async Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var absolute = now.AddDays(options.Value.RefreshTokenAbsoluteDays);
        var (row, issued) = NewToken(userId, Guid.CreateVersion7(), absolute, now);

        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);
        return issued;
    }

    public async Task<RefreshRotationResult> RotateAsync(string rawToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(rawToken);
        var current = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
            return new(RefreshRotationStatus.Invalid, null, null);

        if (current.RevokedAt is not null)
        {
            await RevokeFamilyCoreAsync(current.FamilyId, "reuse_detected", now, ct);
            return new(RefreshRotationStatus.Reused, current.UserId, null);
        }

        if (now >= current.ExpiresAt || now >= current.AbsoluteExpiresAt)
            return new(RefreshRotationStatus.Invalid, null, null);

        var (replacement, issued) = NewToken(current.UserId, current.FamilyId, current.AbsoluteExpiresAt, now);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Conditional update: if a concurrent request rotated this token first, 0 rows match and we treat this
        // presentation as reuse rather than minting a second live token from the same parent.
        var revoked = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, "rotated")
                .SetProperty(t => t.ReplacedById, replacement.Id), ct);

        if (revoked == 0)
        {
            await RevokeFamilyCoreAsync(current.FamilyId, "reuse_detected", now, ct);
            await tx.CommitAsync(ct);
            return new(RefreshRotationStatus.Reused, current.UserId, null);
        }

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(RefreshRotationStatus.Rotated, current.UserId, issued);
    }

    public async Task RevokeFamilyAsync(string rawToken, string reason, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(ct);
        if (familyId is not null)
            await RevokeFamilyCoreAsync(familyId.Value, reason, clock.GetUtcNow(), ct);
    }

    private Task<int> RevokeFamilyCoreAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), ct);

    private (RefreshToken Row, IssuedRefreshToken Issued) NewToken(
        Guid userId, Guid familyId, DateTimeOffset absoluteExpiresAt, DateTimeOffset now)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var sliding = now.AddDays(options.Value.RefreshTokenDays);
        var expiresAt = sliding < absoluteExpiresAt ? sliding : absoluteExpiresAt;

        var row = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = expiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
        };
        return (row, new IssuedRefreshToken(raw, expiresAt));
    }

    internal static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
