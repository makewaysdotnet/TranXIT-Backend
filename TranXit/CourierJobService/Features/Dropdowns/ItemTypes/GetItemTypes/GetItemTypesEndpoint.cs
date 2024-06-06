using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.ItemTypes.GetItemTypes;

public class GetItemTypesEndpoint : CarterModule
{
    public GetItemTypesEndpoint()
    : base("/courierjobservice")
    { }
    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/item-types", async (ISender sender) =>
        {
            var result = await sender.Send(new ItemTypes.Query());

            return Results.Ok(result);
        }).RequireAuthorization()
        .WithTags("Dropdowns")
        .WithOpenApi()
        .Produces<Result<List<ItemTypeResult>>>((int)HttpStatusCode.OK);
    }
}
public class ItemTypes
{
    public sealed class Query : IRequest<Result<List<ItemTypeResult>>>
    {
    }
    internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
        : IRequestHandler<Query, Result<List<ItemTypeResult>>>
    {
        public async Task<Result<List<ItemTypeResult>>> Handle(Query request,
            CancellationToken cancellationToken)
            => await jobDbContext.ItemTypes
            .AsNoTracking()
            .Select(x => new ItemTypeResult
            {
                Id = x.Id,
                Name = x.Name
            })
            .ToListAsync(cancellationToken);
    }
}