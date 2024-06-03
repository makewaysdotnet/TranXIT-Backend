using Carter;
using CourierJobService.Database;
using CourierJobService.Features.Jobs.GetJobs;
using CourierJobService.Requests;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Bids.GetJobUserBids;

public class GetJobUserBidsEndpoint : CarterModule
{
	public GetJobUserBidsEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/bids/{jobId:int}", async (int jobId,
			int page,
			int pageSize,
			IHttpContextAccessor httpContext,
			ISender sender) =>
		{
			var result = await sender.Send(new GetJobUserBids.Query
			{
				JobId = jobId,
				Page = page,
				PageSize = pageSize,
				UserId = HttpContextUser.GetCurrentUserId(httpContext)
			});

			return Results.Ok(result);
		}).RequireAuthorization("CustomerPolicy")
		.WithTags("Bids")
		.WithOpenApi()
		.Produces<Result<Pagination<JobUserBidResult>>>((int)HttpStatusCode.OK);
	}
}

public class GetJobUserBids
{
	public sealed class Query : IRequest<Result<Pagination<JobUserBidResult>>>
	{
		public required int JobId { get; set; }
		public required int Page { get; set; }
		public required int PageSize { get; set; }
		public required int UserId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext,
		IBus messageBus)
		: IRequestHandler<Query, Result<Pagination<JobUserBidResult>>>
	{
		public async Task<Result<Pagination<JobUserBidResult>>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var bidsQuery = jobDbContext.Biddings
				.Where(x => x.JobId == request.JobId && x.UserId == request.UserId)
				.Select(x => new JobUserBidResult
				{
					BidId = x.Id,
					CourierId = x.UserId
				})
				.AsNoTracking()
				.AsQueryable();
			if (bidsQuery is null)
			{
				return new Error("Bids not found");
			}
			var paginatedResponse = await Pagination<JobUserBidResult>
				.CreateAsync(bidsQuery, request.Page, request.PageSize);
			foreach (var item in paginatedResponse.Items)
			{
				var user = await UserRequest.GetUserAsync(item!.CourierId, messageBus);
				item.CourierName = user?.UserName!;
			}
			return paginatedResponse;
		}
	}
}