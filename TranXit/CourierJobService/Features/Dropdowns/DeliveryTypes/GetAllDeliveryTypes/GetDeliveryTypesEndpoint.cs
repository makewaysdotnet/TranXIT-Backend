using Carter;
using CourierJobService.Database;
using CourierJobService.Features.Dropdowns.DeliveryTypes.SharedResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.DeliveryTypes.GetAllDeliveryTypes;

public class GetDeliveryTypesEndpoint : CarterModule
{
    public GetDeliveryTypesEndpoint()
    : base("/api")
    { }
    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/delivery-types", async (ISender sender) =>
        {
            var result = await sender.Send(new GetAllDeliveryTypes.Query());

            return Results.Ok(result);
        }).RequireAuthorization()
		.WithTags("Dropdowns")
		.WithOpenApi()
        .Produces<Result<List<DeliveryTypeResult>>>((int)HttpStatusCode.OK);
    }
}
public class GetAllDeliveryTypes
{
    public sealed class Query : IRequest<Result<List<DeliveryTypeResult>>>
    {
    }
    internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
        : IRequestHandler<Query, Result<List<DeliveryTypeResult>>>
    {
        public async Task<Result<List<DeliveryTypeResult>>> Handle(Query request,
            CancellationToken cancellationToken)
            => await jobDbContext.DeliveryTypes
            .AsNoTracking()
            .Select(x => new DeliveryTypeResult
            {
                Id = x.Id,
                Name = x.Name,
                NoOfDays = x.NoOfDays,
            })
            .ToListAsync(cancellationToken);
    }
}