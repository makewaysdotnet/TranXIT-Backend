using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJob;

public class GetJobEndpoint : CarterModule
{
	public GetJobEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs/{customerId:int}", async (int customerId, ISender sender) =>
		{
			var query = new GetJob.Query { UserId = customerId };
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<List<CustomerJobResult>>>((int)HttpStatusCode.OK);
	}
}
public class GetJob
{
	public sealed class Query : IRequest<Result<List<CustomerJobResult>>>
	{
		public int UserId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<List<CustomerJobResult>>>
	{
		public async Task<Result<List<CustomerJobResult>>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var jobResponse = await jobDbContext.Jobs
				.Select(x => new CustomerJobResult
				{
					Id = x.Id,
					CreatedOnUtc = x.CreatedOnUtc,
					CustomerId = x.UserId,
					DestinationCity = x.DestinationCity!.CityName,
					DestinationCountry = x.DestinationCountry!.CountryName,
					OriginCountry = x.OriginCountry!.CountryName,
					OriginCity = x.OriginCity!.CityName,
					JobNumber = x.JobNumber,
					OriginAddress = x.OriginAddress,
					DestinationAddress = x.DestinationAddress,
					StatusId = x.JobStatusId,
					Status = x.JobStatus!.Status,
				})
				.Where(x => x.CustomerId == request.UserId)
				.OrderByDescending(x => x.CreatedOnUtc)
				.ToListAsync(cancellationToken);

			if (jobResponse.Count is 0)
			{
				return new Error("Jobs not found");
			}
			return jobResponse;
		}
	}
}
