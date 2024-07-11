using Carter;
using CourierJobService.Database;
using CourierJobService.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobBidStats;

public class GetJobBidStatsEndpoint : CarterModule
{
	public GetJobBidStatsEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs/{jobId:int}/bid-stats", async (int jobId, ISender sender) =>
		{
			var result = await sender.Send(new GetJobStats.Query { JobId = jobId });

			return Results.Ok(result);
		}).RequireAuthorization("CourierPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<JobBidStatsResult>>((int)HttpStatusCode.OK);
	}
}
public class GetJobStats
{
	public sealed class Query : IRequest<Result<JobBidStatsResult>>
	{
		public required int JobId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Query, Result<JobBidStatsResult>>
	{
		public async Task<Result<JobBidStatsResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var job = await jobDbContext.Jobs
				.Include(x => x.Biddings).ThenInclude(y => y.JobStatus)
				.Include(x => x.JobStatus)
				.AsSplitQuery()
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == request.JobId, cancellationToken);
			var userId = HttpContextUser.GetCurrentUserId(httpContext);
			return new JobBidStatsResult
			{
				JobId = job!.Id,
				JobNumber = job.JobNumber,
				RemainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, DateTime.UtcNow),
				TotalBids = job.Biddings?.Count,
				Status = JobsHelper.GetJobStatus(job, job.Biddings, userId),
				AverageBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Average(x => x.TotalAmount) : 0,
				MaxBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Max(x => x.TotalAmount) : 0,
				MinBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Min(x => x.TotalAmount) : 0,
				CreatedOnUtc = job.CreatedOnUtc,
			};
		}
	}
}