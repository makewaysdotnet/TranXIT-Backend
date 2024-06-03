using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobBidStats;

public class GetJobBidStatsEndpoint : CarterModule
{
	public GetJobBidStatsEndpoint()
	: base("/courierjobservice")
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
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<JobBidStatsResult>>
	{
		public async Task<Result<JobBidStatsResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var job = await jobDbContext.Jobs
				.Include(x => x.Biddings)
				.Include(x => x.JobStatus)
				.AsSplitQuery()
				.FirstOrDefaultAsync(x => x.Id == request.JobId, cancellationToken);
			var currentTime = DateTime.UtcNow;
			return new JobBidStatsResult
			{
				JobId = job!.Id,
				JobNumber = job.JobNumber,
				RemainingTime = job.ExpiryDateUtc.HasValue &&
					(job.ExpiryDateUtc - currentTime)!.Value.TotalSeconds > 0 ?
					(job.ExpiryDateUtc - currentTime)!.Value.TotalSeconds : 0,
				TotalBids = job.Biddings.Count,
				Status = job.JobStatus?.Status!,
				AverageBid = job.Biddings?.Average(x => x.TotalAmount),
				MaxBid = job.Biddings?.Max(x => x.TotalAmount),
				MinBid = job.Biddings?.Min(x => x.TotalAmount),
				CreatedOnUtc = job.CreatedOnUtc,
			};
		}
	}
}