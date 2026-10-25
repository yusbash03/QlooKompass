using FluentValidation;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;
using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Experiments;

namespace KultureCompass.Application.Plans;

/// <summary>contracts/openapi.yaml UpdateExperimentRequest — only the tracking fields are mutable.</summary>
public sealed record UpdateExperimentRequest(ExperimentStatus? Status, string? ObservedResult, string? ResultNotes);

public sealed class UpdateExperimentRequestValidator : AbstractValidator<UpdateExperimentRequest>
{
    public UpdateExperimentRequestValidator()
    {
        RuleFor(r => r.Status).NotNull().IsInEnum();
        RuleFor(r => r.ObservedResult).MaximumLength(200);
        RuleFor(r => r.ResultNotes).MaximumLength(1000);
    }
}

public sealed record UpdateExperimentCommand(Guid PlanId, string ExperimentId, string? IfMatch, UpdateExperimentRequest Body);

/// <summary>PATCH /growth-plans/{id}/experiments/{experimentId} (stretch): If-Match against the experiment row's xmin.</summary>
public sealed class UpdateExperimentHandler(
    IGrowthPlanStore plans, ICurrentUser user, IValidator<UpdateExperimentRequest> validator, TimeProvider clock)
{
    public async Task<Result<ExperimentDto>> HandleAsync(UpdateExperimentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.IfMatch))
            return UpdateProfileHandler.PreconditionRequired;

        var validation = await validator.ValidateAsync(cmd.Body, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var plan = await plans.GetForUpdateAsync(cmd.PlanId, user.OwnerId, ct).ConfigureAwait(false);
        var experiment = plan?.Experiments.FirstOrDefault(e => e.Id == cmd.ExperimentId);
        if (experiment is null)
            return AppError.NotFound();

        if (!UpdateProfileHandler.TryParseETag(cmd.IfMatch, out var expected) || expected != experiment.Version)
            return UpdateProfileHandler.VersionMismatch;

        experiment.UpdateTracking(cmd.Body.Status!.Value, cmd.Body.ObservedResult, cmd.Body.ResultNotes, clock.GetUtcNow());
        if (!await plans.TrySaveChangesAsync(ct).ConfigureAwait(false))
            return UpdateProfileHandler.VersionMismatch;

        return Result<ExperimentDto>.Ok(PlanMapper.ToExperiment(experiment));
    }
}

/// <summary>GET /experiments: the cross-plan hub for the launch → measure loop.</summary>
public sealed class ListExperimentsHandler(IGrowthPlanStore plans, ICurrentUser user, IValidator<PageQuery> validator)
{
    public async Task<Result<PagedResult<ExperimentSummaryDto>>> HandleAsync(ListExperimentsQuery query, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(new PageQuery(query.Page, query.PageSize), ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var (items, total) = await plans.ListExperimentsAsync(user.OwnerId, query.Page, query.PageSize, query.Status, ct).ConfigureAwait(false);
        return Result<PagedResult<ExperimentSummaryDto>>.Ok(new(items, query.Page, query.PageSize, total));
    }
}
