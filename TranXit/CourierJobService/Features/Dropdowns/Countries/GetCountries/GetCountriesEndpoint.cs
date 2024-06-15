using Carter;
using CourierJobService.Database;
using CourierJobService.Features.Dropdowns.Countries.SharedResult;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Dropdowns.Countries.GetCountries;

public class GetCountriesEndpoint : CarterModule
{
	public GetCountriesEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/countries", async (ISender sender) =>
		{
			var result = await sender.Send(new GetCountries.Query());

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithTags("Dropdowns")
		.WithOpenApi()
		.Produces<Result<List<CountryResult>>>((int)HttpStatusCode.OK);
	}
}
public class GetCountries
{
	public sealed class Query : IRequest<Result<List<CountryResult>>>
	{
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<List<CountryResult>>>
	{
		public async Task<Result<List<CountryResult>>> Handle(Query request,
			CancellationToken cancellationToken)
			=> await jobDbContext.Countries
			.AsNoTracking()
			.Select(x => new CountryResult
			{
				Id = x.Id,
				Name = x.CountryName,
			})
			.ToListAsync(cancellationToken);
	}
}