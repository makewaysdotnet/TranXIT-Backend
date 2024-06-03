using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobStats;

public class GetJobStatsEndpoint : CarterModule
{
	public GetJobStatsEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs/stats", async (ISender sender) =>
		{
			var result = await sender.Send(new GetJobStats.Query());

			return Results.Ok(result);
		}).RequireAuthorization("CourierPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<JobStatsResult>>((int)HttpStatusCode.OK);
	}
}
public class GetJobStats
{
	public sealed class Query : IRequest<Result<JobStatsResult>>
	{
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Query, Result<JobStatsResult>>
	{
		public async Task<Result<JobStatsResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var userId = HttpContextUser.GetCurrentUserId(httpContext);
			List<string> jobStatuses = ["Open", "Closed", "Lost"];
			var jobs = await jobDbContext.Jobs
				.Include(x => x.JobStatus)
				.Where(x => x.UserId == userId && jobStatuses.Contains(x.JobStatus!.Status!))
				.AsSplitQuery()
				.AsNoTracking()
				.ToListAsync();

			if (!jobs.Any())
			{
				return new JobStatsResult();
			}

			return new JobStatsResult
			{
				Delivered = jobs.Count(x => x.JobStatus!.Status == "Delivered"),
				InTransit = jobs.Count(x => x.JobStatus!.Status == "InTransit"),
				Won = jobs.Count(x => x.JobStatus!.Status == "Won"),
				TotalShipments = jobs.Count(x => x.JobStatus!.Status == "Delivered") +
					jobs.Count(x => x.JobStatus!.Status == "InTransit"),
			};
		}
	}
}