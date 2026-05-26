using Carter;
using CourierJobService.Database;
using CourierJobService.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;
using JobStatusEnum = CourierJobService.Enums.JobStatusEnum;

namespace CourierJobService.Features.Jobs.GetJobStats;

public class GetJobStatsEndpoint : CarterModule
{
	public GetJobStatsEndpoint()
	: base("/api")
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
			var jobStatuses = JobsHelper.GetSuccessfullJobStatuses();
			var biddings = await jobDbContext.Biddings
				.Where(x => x.UserId == userId &&
					x.JobStatusId.HasValue &&
					jobStatuses.Contains(x.JobStatusId.Value))
				.AsSplitQuery()
				.AsNoTracking()
				.ToListAsync(cancellationToken);

			if (!biddings.Any())
			{
				return new JobStatsResult();
			}

			return new JobStatsResult
			{
				Delivered = biddings.Count(x => x.JobStatusId == (int)JobStatusEnum.Delivered),
				InTransit = biddings.Count(x => x.JobStatusId == (int)JobStatusEnum.InTransit),
				Won = biddings.Count(x => x.JobStatusId == (int)JobStatusEnum.Won),
				TotalShipments = biddings.Count(x => x.JobStatusId == (int)JobStatusEnum.Delivered) +
					biddings.Count(x => x.JobStatusId == (int)JobStatusEnum.InTransit),
			};
		}
	}
}
