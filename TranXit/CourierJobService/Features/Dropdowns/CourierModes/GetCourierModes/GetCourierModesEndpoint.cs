using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.CourierModes.GetCourierModes;

public class GetCourierModesEndpoint : CarterModule
{
    public GetCourierModesEndpoint()
    : base("/courierjobservice")
    { }
    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/courier-modes", async (ISender sender) =>
        {
            var result = await sender.Send(new CourierModes.Query());

            return Results.Ok(result);
        }).RequireAuthorization()
        .WithTags("Dropdowns")
        .WithOpenApi()
        .Produces<Result<List<CourierModeResult>>>((int)HttpStatusCode.OK);
    }
}
public class CourierModes
{
    public sealed class Query : IRequest<Result<List<CourierModeResult>>>
    {
    }
    internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
        : IRequestHandler<Query, Result<List<CourierModeResult>>>
    {
        public async Task<Result<List<CourierModeResult>>> Handle(Query request,
            CancellationToken cancellationToken)
            => await jobDbContext.CourierModes
            .AsNoTracking()
            .Select(x => new CourierModeResult
            {
                Id = x.Id,
                Name = x.Name,
            })
            .ToListAsync(cancellationToken);
    }
}