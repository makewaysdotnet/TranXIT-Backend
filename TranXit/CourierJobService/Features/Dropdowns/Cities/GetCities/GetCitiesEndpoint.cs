using Carter;
using CourierJobService.Database;
using CourierJobService.Features.Dropdowns.Cities.SharedResult;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.Cities.GetCities;

public class GetCitiesEndpoint : CarterModule
{
	public GetCitiesEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/cities/{countryId:int}", async (int countryId, ISender sender) =>
		{
			var result = await sender.Send(new GetCities.Query { CountryId = countryId });

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithTags("Dropdowns")
		.WithOpenApi()
		.Produces<Result<List<CityResult>>>((int)HttpStatusCode.OK);
	}
}
public class GetCities
{
	public sealed class Query : IRequest<Result<List<CityResult>>>
	{
		public required int CountryId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<List<CityResult>>>
	{
		public async Task<Result<List<CityResult>>> Handle(Query request,
			CancellationToken cancellationToken)
			=> await jobDbContext.Cities
			.Where(x => x.CountryId == request.CountryId)
			.AsNoTracking()
			.Select(x => new CityResult
			{
				Id = x.Id,
				Name = x.CityName,
			})
			.ToListAsync(cancellationToken);
	}
}