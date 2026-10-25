using KultureCompass.Api.Http;
using KultureCompass.Application.Common;
using KultureCompass.Application.Profiles;
using Microsoft.AspNetCore.Mvc;

namespace KultureCompass.Api.Endpoints;

public static class BusinessProfileEndpoints
{
    public const string BasePath = "/api/v1/business-profiles";

    public static void MapBusinessProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("BusinessProfiles");

        group.MapPost("", async (BusinessProfileRequest body, CreateProfileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult(dto =>
                    new ETagResult(TypedResults.Created($"{BasePath}/{dto.Id}", dto), dto.Version)))
            .WithName("createBusinessProfile")
            .Produces<BusinessProfileDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("", async (ListProfilesHandler handler, CancellationToken ct, int page = 1, int pageSize = 20) =>
                (await handler.HandleAsync(new PageQuery(page, pageSize), ct)).ToHttpResult(TypedResults.Ok))
            .WithName("listBusinessProfiles")
            .Produces<PagedResult<BusinessProfileSummary>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (Guid id, GetProfileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult(dto => new ETagResult(TypedResults.Ok(dto), dto.Version)))
            .WithName("getBusinessProfile")
            .Produces<BusinessProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, BusinessProfileRequest body,
                UpdateProfileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateProfileCommand(id, ifMatch, body), ct))
                    .ToHttpResult(dto => new ETagResult(TypedResults.Ok(dto), dto.Version)))
            .WithName("updateBusinessProfile")
            .Produces<BusinessProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }
}
