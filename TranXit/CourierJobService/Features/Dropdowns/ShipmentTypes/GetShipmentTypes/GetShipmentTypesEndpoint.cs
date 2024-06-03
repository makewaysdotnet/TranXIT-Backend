using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.ShipmentTypes.GetShipmentTypes;

public class GetShipmentTypesEndpoint : CarterModule
{
	public GetShipmentTypesEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/shipment-types", async (ISender sender) =>
		{
			var result = await sender.Send(new GetShipmentTypes.Query());

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithTags("Dropdowns")
		.WithOpenApi()
		.Produces<Result<List<ShipmentTypeResult>>>((int)HttpStatusCode.OK);
	}
}
public class GetShipmentTypes
{
	public sealed class Query : IRequest<Result<List<ShipmentTypeResult>>>
	{
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<List<ShipmentTypeResult>>>
	{
		public async Task<Result<List<ShipmentTypeResult>>> Handle(Query request,
			CancellationToken cancellationToken)
			=> await jobDbContext.CargoModes
			.Select(x => new ShipmentTypeResult
			{
				Id = x.Id,
				Name = x.Name,
			})
			.AsNoTracking()
			.ToListAsync(cancellationToken);
	}
}