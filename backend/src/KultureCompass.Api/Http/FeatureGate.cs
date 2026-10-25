using KultureCompass.Application.Common;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.Http;

/// <summary>503 <c>feature_disabled</c> when a Features:* flag is off (01-api-contract §5). Runs before auth-dependent
/// handler work, so a disabled feature never touches the database or a provider.</summary>
public static class FeatureGate
{
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, Func<FeatureOptions, bool> isEnabled, string featureName)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var features = context.HttpContext.RequestServices.GetRequiredService<IOptions<FeatureOptions>>().Value;
            return isEnabled(features)
                ? await next(context)
                : new AppError("feature_disabled", $"{featureName} is disabled on this server.", ErrorKind.Unavailable).ToProblem();
        });
}
