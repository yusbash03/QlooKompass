using KultureCompass.Api.Http;
using KultureCompass.Application.Common;

namespace KultureCompass.Api.Auth;

/// <summary>CSRF guard for cookie-authenticated endpoints (/auth/refresh, /auth/logout): a cross-site form can't set
/// custom headers, so requiring <c>X-Requested-With: kc-web</c> blocks forged requests that would carry kc_rt.</summary>
public sealed class RequireKcWebHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Requested-With";
    public const string ExpectedValue = "kc-web";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!string.Equals(context.HttpContext.Request.Headers[HeaderName], ExpectedValue, StringComparison.Ordinal))
            return AppError.Validation(HeaderName, $"Header {HeaderName}: {ExpectedValue} is required.").ToProblem();

        return await next(context);
    }
}
