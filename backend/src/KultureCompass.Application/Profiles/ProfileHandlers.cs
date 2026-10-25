using System.Globalization;
using FluentValidation;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Profiles;

public sealed class CreateProfileHandler(
    IBusinessProfileStore store, ICurrentUser user, IValidator<BusinessProfileRequest> validator, TimeProvider clock)
{
    public async Task<Result<BusinessProfileDto>> HandleAsync(BusinessProfileRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var profile = BusinessProfile.Create(
            user.OwnerId, request.BrandName!, request.Description, request.Category!.Value, request.CategoryOther,
            request.ProductsDescription!, request.TargetMarket, request.PrimaryLocation, request.MarketingObjective!.Value,
            request.ObjectiveNotes, request.BrandIdentity, ToReferenceEntities(request), request.BudgetRange,
            clock.GetUtcNow());

        await store.AddAsync(profile, ct).ConfigureAwait(false);
        await store.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<BusinessProfileDto>.Ok(BusinessProfileDto.From(profile));
    }

    internal static IReadOnlyList<ReferenceEntity> ToReferenceEntities(BusinessProfileRequest request) =>
        [.. (request.ReferenceEntities ?? []).Select(r => ReferenceEntity.Create(r.Name!.Trim(), r.KindHint!.Value))];
}

public sealed class GetProfileHandler(IBusinessProfileStore store, ICurrentUser user)
{
    public async Task<Result<BusinessProfileDto>> HandleAsync(Guid id, CancellationToken ct)
    {
        var profile = await store.GetByIdAsync(id, user.OwnerId, ct).ConfigureAwait(false);
        return profile is null ? AppError.NotFound() : Result<BusinessProfileDto>.Ok(BusinessProfileDto.From(profile));
    }
}

public sealed class ListProfilesHandler(IBusinessProfileStore store, ICurrentUser user, IValidator<PageQuery> validator)
{
    public async Task<Result<PagedResult<BusinessProfileSummary>>> HandleAsync(PageQuery query, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var (items, total) = await store.ListPagedAsync(user.OwnerId, query.Page, query.PageSize, ct).ConfigureAwait(false);
        return Result<PagedResult<BusinessProfileSummary>>.Ok(new(items, query.Page, query.PageSize, total));
    }
}

public sealed record UpdateProfileCommand(Guid Id, string? IfMatch, BusinessProfileRequest Body);

public sealed class UpdateProfileHandler(
    IBusinessProfileStore store, ICurrentUser user, IValidator<BusinessProfileRequest> validator, TimeProvider clock)
{
    public static readonly AppError PreconditionRequired =
        new("precondition_required", "If-Match header is required. Send the ETag from your last GET.", ErrorKind.PreconditionRequired);

    public static readonly AppError VersionMismatch =
        new("version_mismatch", "This profile was changed since you loaded it. Reload and try again.", ErrorKind.PreconditionFailed);

    public async Task<Result<BusinessProfileDto>> HandleAsync(UpdateProfileCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.IfMatch))
            return PreconditionRequired;

        var validation = await validator.ValidateAsync(cmd.Body, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var profile = await store.GetByIdAsync(cmd.Id, user.OwnerId, ct).ConfigureAwait(false);
        if (profile is null)
            return AppError.NotFound();

        if (!TryParseETag(cmd.IfMatch, out var expectedVersion) || expectedVersion != profile.Version)
            return VersionMismatch;

        var body = cmd.Body;
        profile.Update(
            body.BrandName!, body.Description, body.Category!.Value, body.CategoryOther, body.ProductsDescription!,
            body.TargetMarket, body.PrimaryLocation, body.MarketingObjective!.Value, body.ObjectiveNotes, body.BrandIdentity,
            CreateProfileHandler.ToReferenceEntities(body), body.BudgetRange, clock.GetUtcNow());

        // The version check above catches stale clients; the row-version save catches a concurrent writer between
        // our read and write.
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false))
            return VersionMismatch;

        return Result<BusinessProfileDto>.Ok(BusinessProfileDto.From(profile));
    }

    /// <summary>Accepts <c>"123"</c> (what we emit), weak <c>W/"123"</c>, or a bare <c>123</c>.</summary>
    internal static bool TryParseETag(string value, out uint version)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal))
            trimmed = trimmed[2..];
        return uint.TryParse(trimmed.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}
