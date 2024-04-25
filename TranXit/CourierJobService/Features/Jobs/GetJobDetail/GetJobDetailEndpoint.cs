using Carter;
using CourierJobService.Database;
using CourierJobService.Requests;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobDetail;

public class GetJobDetailEndpoint : CarterModule
{
	public GetJobDetailEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/job-details/{jobId:int}", async (int jobId,
			ISender sender,
			IBus messageBus) =>
		{
			var query = new GetJobDetail.Query { jobId = jobId, messageBus = messageBus };
			var result = await sender.Send(query);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<JobDetailResult>>((int)HttpStatusCode.OK)
		.Produces<Result<JobDetailResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class GetJobDetail
{
	public sealed class Query : IRequest<Result<JobDetailResult>>
	{
		public required int jobId { get; set; }
		public required IBus messageBus { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<JobDetailResult>>
	{
		public async Task<Result<JobDetailResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var jobDetailResponse = await jobDbContext.Jobs
				.Select(x => new JobDetailResult
				{
					JobId = x.Id,
					UserId = x.UserId,
					DestinationAddress = x.DestinationAddress!,
					CargoMode = x.CargoMode!.Name!,
					CourierMode = x.CourierMode!.Name!,
					JobItems = x.JobItems!.Select(y => new JobItemResult
					{
						JobItemId = y.Id,
						ItemName = y.Name ?? string.Empty,
						DeclaredValue = y.DeclaredValue ?? 0.0,
						Description = y.Description ?? string.Empty,
						Quantity = y.Quantity ?? 0,
						Size = y.Dimensions ?? string.Empty,
						Weight = y.Weight ?? 0.0,
					})
				})
				.AsSplitQuery()
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.JobId == request.jobId, cancellationToken);
			if (jobDetailResponse is null)
			{
				return new Error("Job Detail not found");
			}
			var userResult = await UserRequest.GetUserAsync(jobDetailResponse!.UserId, request.messageBus);
			if (userResult is null)
			{
				return new Error("User not found");
			}
			jobDetailResponse.CustomerName = userResult.UserName!;
			return jobDetailResponse;
		}
	}
}
