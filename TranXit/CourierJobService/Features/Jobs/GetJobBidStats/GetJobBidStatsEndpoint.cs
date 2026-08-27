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
		app.MapGet("/jobs/{jobId:int}/bid-stats", async (
			int jobId,
			ISender sender,
			IHttpContextAccessor httpContext) =>
		{
			var result = await sender.Send(new GetJobStats.Query
			{
				JobId = jobId,
				UserId = HttpContextUser.GetCurrentUserId(httpContext)
			});

			if (!result.isSuccess)
			{
				if (result.error.Contains(GetJobStats.ForbiddenError))
				{
					return Results.Forbid();
				}
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization("CourierPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<JobBidStatsResult>>((int)HttpStatusCode.OK);
	}
}
public class GetJobStats
{
	public const string ForbiddenError = "Forbidden";

	public sealed class Query : IRequest<Result<JobBidStatsResult>>
	{
		public required int JobId { get; set; }
		public int UserId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<JobBidStatsResult>>
	{
		public async Task<Result<JobBidStatsResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var job = await jobDbContext.Jobs
				.Where(JobAccess.VisibleToCourier(request.UserId, DateTime.UtcNow))
				.Include(x => x.Biddings).ThenInclude(y => y.JobStatus)
				.Include(x => x.JobStatus)
				.AsSplitQuery()
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == request.JobId, cancellationToken);
			if (job is null)
			{
				if (await jobDbContext.Jobs.AnyAsync(
					candidate => candidate.Id == request.JobId,
					cancellationToken))
				{
					return new Error(ForbiddenError);
				}
				return new Error("Job not found");
			}

			return new JobBidStatsResult
			{
				JobId = job!.Id,
				JobNumber = job.JobNumber,
				RemainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, DateTime.UtcNow),
				TotalBids = job.Biddings?.Count,
				Status = JobsHelper.GetJobStatus(job, job.Biddings, request.UserId).Item2,
				AverageBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Average(x => x.TotalAmount) : null,
				MaxBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Max(x => x.TotalAmount) : null,
				MinBid = job.Biddings?.Count > 0 ?
					job.Biddings?.Min(x => x.TotalAmount) : null,
				CreatedOnUtc = job.CreatedOnUtc,
			};
		}
	}
}
