using Carter;
using CourierJobService.Database;
using CourierJobService.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJob;

public class GetJobEndpoint : CarterModule
{
	public GetJobEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs/{customerId:int}", async (int customerId,
			ISender sender,
			IHttpContextAccessor httpContext) =>
		{
			var userId = HttpContextUser.GetCurrentUserId(httpContext);
			if (!userId.Equals(customerId))
			{
				return Results.Unauthorized();
			}
			var query = new GetJob.Query { UserId = customerId };
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization("CustomerPolicy")
		.WithTags("Jobs")
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
				.Where(x => x.UserId == request.UserId)
				.OrderByDescending(x => x.CreatedOnUtc)
				.Include(x => x.DestinationCity)
				.Include(x => x.DestinationCountry)
				.Include(x => x.OriginCity)
				.Include(x => x.OriginCountry)
				.Include(x => x.JobStatus)
				.Include(x => x.Biddings).ThenInclude(y => y.JobStatus)
				.AsSplitQuery()
				.AsNoTracking()
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
					StatusId = JobsHelper.GetJobStatus(x, x.Biddings, null).Item1,
					Status = JobsHelper.GetJobStatus(x, x.Biddings, null).Item2,
				})
				.ToListAsync(cancellationToken);

			if (jobResponse.Count is 0)
			{
				return new Error("Jobs not found");
			}
			return jobResponse;
		}
	}
}
