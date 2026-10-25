using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;
using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Plans;
using Microsoft.Extensions.Options;

namespace KultureCompass.Application.Plans;

public sealed class PlanQuotaSettings
{
    public int DailyLimitPerUser { get; set; } = 10;
}

public sealed class StartGrowthPlanRequestValidator : AbstractValidator<StartGrowthPlanRequest>
{
    public StartGrowthPlanRequestValidator()
    {
        RuleFor(r => r.BusinessProfileId).NotNull().NotEqual(Guid.Empty);
        RuleFor(r => r.Options!.MaxCampaigns).InclusiveBetween(1, 3).When(r => r.Options?.MaxCampaigns is not null);
    }
}

public static class IdempotencyHasher
{
    /// <summary>SHA-256 over the normalised request (defaults applied), so "options omitted" and "options = defaults"
    /// are the same intent.</summary>
    public static string Hash(Guid businessProfileId, GenerationOptions options) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{businessProfileId:N}|{options.IncludeCollaborations}|{options.IncludeCampaigns}|{options.MaxCampaigns}"))));
}

public sealed record StartGrowthPlanCommand(string? IdempotencyKey, StartGrowthPlanRequest Body);

/// <summary>POST /growth-plans (01-api-contract §3.1): validation → idempotent replay → ownership → one active plan per
/// profile → daily quota → insert Queued → enqueue. The DB's unique (owner_id, idempotency_key) constraint settles races.</summary>
public sealed class StartGrowthPlanHandler(
    IBusinessProfileStore profiles,
    IGrowthPlanStore plans,
    IGenerationQueue queue,
    ICurrentUser user,
    IValidator<StartGrowthPlanRequest> validator,
    IOptions<PlanQuotaSettings> quota,
    TimeProvider clock)
{
    public static readonly AppError KeyMissing =
        new("idempotency_key_missing", "An Idempotency-Key header (UUID) is required.", ErrorKind.Validation);

    public static readonly AppError KeyReused =
        new("idempotency_key_reused", "This Idempotency-Key was already used for a different request.", ErrorKind.Conflict);

    public static readonly AppError PlanInProgress =
        new("plan_in_progress", "A plan for this business profile is already being generated.", ErrorKind.Conflict);

    public async Task<Result<StartedPlan>> HandleAsync(StartGrowthPlanCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
            return KeyMissing;
        if (!Guid.TryParse(cmd.IdempotencyKey, out var parsedKey))
            return AppError.Validation("Idempotency-Key", "Idempotency-Key must be a UUID.");
        var key = parsedKey.ToString("D");

        var validation = await validator.ValidateAsync(cmd.Body, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var profileId = cmd.Body.BusinessProfileId!.Value;
        var options = (cmd.Body.Options ?? new GenerationOptionsInput(null, null, null)).ToDomain();
        var hash = IdempotencyHasher.Hash(profileId, options);

        if (await plans.GetByIdempotencyKeyAsync(user.OwnerId, key, ct).ConfigureAwait(false) is { } existing)
            return Replay(existing, hash);

        var profile = await profiles.GetByIdAsync(profileId, user.OwnerId, ct).ConfigureAwait(false);
        if (profile is null)
            return AppError.NotFound();

        if (await plans.HasActivePlanForProfileAsync(profileId, ct).ConfigureAwait(false))
            return PlanInProgress;

        var now = clock.GetUtcNow();
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var used = await plans.CountCreatedSinceAsync(user.OwnerId, startOfDay, ct).ConfigureAwait(false);
        if (used >= quota.Value.DailyLimitPerUser)
        {
            return new AppError("generation_quota_exceeded",
                $"Daily limit of {quota.Value.DailyLimitPerUser} plans reached. It resets at midnight UTC.",
                ErrorKind.RateLimited, RetryAfterSeconds: (int)Math.Ceiling((startOfDay.AddDays(1) - now).TotalSeconds));
        }

        var plan = GrowthPlan.CreateQueued(user.OwnerId, profileId, key, hash, BriefSnapshot.FromProfile(profile), options, now);
        if (!await plans.TryAddAsync(plan, ct).ConfigureAwait(false))
        {
            // Lost a race with a concurrent request carrying the same key.
            var winner = await plans.GetByIdempotencyKeyAsync(user.OwnerId, key, ct).ConfigureAwait(false);
            return winner is null ? KeyReused : Replay(winner, hash);
        }

        await queue.EnqueueAsync(plan.Id, ct).ConfigureAwait(false);
        return Result<StartedPlan>.Ok(new StartedPlan(GrowthPlanAcceptedDto.From(plan), IsTerminalReplay: false));
    }

    private static Result<StartedPlan> Replay(GrowthPlan existing, string requestHash) =>
        existing.RequestHash == requestHash
            ? Result<StartedPlan>.Ok(new StartedPlan(GrowthPlanAcceptedDto.From(existing), existing.IsTerminal))
            : KeyReused;
}

public sealed class GetGrowthPlanHandler(IGrowthPlanStore plans, ICurrentUser user)
{
    public async Task<Result<GrowthPlanDto>> HandleAsync(Guid id, CancellationToken ct)
    {
        var plan = await plans.GetByIdAsync(id, user.OwnerId, ct).ConfigureAwait(false);
        return plan is null ? AppError.NotFound() : Result<GrowthPlanDto>.Ok(PlanMapper.ToDto(plan));
    }
}

public sealed class GetGrowthPlanStatusHandler(IGrowthPlanStore plans, ICurrentUser user)
{
    public async Task<Result<GrowthPlanStatusDto>> HandleAsync(Guid id, CancellationToken ct)
    {
        var plan = await plans.GetStatusByIdAsync(id, user.OwnerId, ct).ConfigureAwait(false);
        return plan is null ? AppError.NotFound() : Result<GrowthPlanStatusDto>.Ok(PlanMapper.ToStatus(plan));
    }
}

public sealed class ListGrowthPlansHandler(IGrowthPlanStore plans, ICurrentUser user, IValidator<PageQuery> validator)
{
    public async Task<Result<PagedResult<GrowthPlanListItem>>> HandleAsync(ListPlansQuery query, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(new PageQuery(query.Page, query.PageSize), ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var (items, total) = await plans.ListPagedAsync(
            user.OwnerId, query.Page, query.PageSize, query.BusinessProfileId, query.Status, ct).ConfigureAwait(false);
        return Result<PagedResult<GrowthPlanListItem>>.Ok(new(items, query.Page, query.PageSize, total));
    }
}

public sealed class GetQuotaHandler(IGrowthPlanStore plans, ICurrentUser user, IOptions<PlanQuotaSettings> quota, TimeProvider clock)
{
    public async Task<Result<QuotaDto>> HandleAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var used = await plans.CountCreatedSinceAsync(user.OwnerId, startOfDay, ct).ConfigureAwait(false);
        var limit = quota.Value.DailyLimitPerUser;
        return Result<QuotaDto>.Ok(new QuotaDto(limit, used, Math.Max(0, limit - used), startOfDay.AddDays(1)));
    }
}
