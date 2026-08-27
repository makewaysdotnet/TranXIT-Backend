using Carter;
using CourierJobService.Database;
using CourierJobService.Enums;
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
	: base("/api")
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
				.Include(x => x.Job)
				.Where(x => x.JobId == request.JobId && x.Job.UserId == request.UserId)
				.AsSplitQuery()
				.AsNoTracking()
				.Select(x => new JobUserBidResult
				{
					BidId = x.Id,
					AcceptedBidProposalId = x.Job.AcceptedBidProposal != null && x.Job.AcceptedBidProposal.BiddingId == x.Id
						? x.Job.AcceptedBidProposalId : null,
					BidStatusId = x.JobStatusId,
					IsJobAwarded = x.Job.AcceptedBidProposalId != null || x.Job.IsJobStatusFromBid == true ||
						x.Job.JobStatusId == (int)JobStatusEnum.Won || x.Job.JobStatusId == (int)JobStatusEnum.InTransit ||
						x.Job.JobStatusId == (int)JobStatusEnum.Delivered ||
						x.Job.Biddings.Any(bid => bid.JobStatusId == (int)JobStatusEnum.Won ||
							bid.JobStatusId == (int)JobStatusEnum.InTransit || bid.JobStatusId == (int)JobStatusEnum.Delivered),
					CanAccept = x.Job.AcceptedBidProposalId == null && x.Job.IsJobStatusFromBid != true &&
						(x.Job.JobStatusId == (int)JobStatusEnum.Open || x.Job.JobStatusId == (int)JobStatusEnum.Bidding) &&
						x.Job.ExpiryDateUtc > DateTime.UtcNow &&
						(x.JobStatusId == null || x.JobStatusId == (int)JobStatusEnum.Open || x.JobStatusId == (int)JobStatusEnum.Bidding) &&
						!x.Job.Biddings.Any(bid => bid.JobStatusId == (int)JobStatusEnum.Won ||
							bid.JobStatusId == (int)JobStatusEnum.InTransit || bid.JobStatusId == (int)JobStatusEnum.Delivered),
					BidMinOffer = x.TotalAmount,
					CourierId = x.UserId,
				})
				.AsQueryable();
			if (bidsQuery is null)
			{
				return new Error("Bids not found");
			}
			var paginatedResponse = await Pagination<JobUserBidResult>
				.CreateAsync(bidsQuery, request.Page, request.PageSize);
			var bidIds = paginatedResponse.Items.Select(item => item.BidId).ToList();
			var bidProposalLookup = await jobDbContext.BiddingProposals
				.AsNoTracking()
				.Where(proposal => proposal.BiddingId.HasValue && bidIds.Contains(proposal.BiddingId.Value))
				.OrderByDescending(proposal => proposal.IsBaseBid == true)
				.ThenBy(proposal => proposal.Total)
				.ThenBy(proposal => proposal.Id)
				.Select(proposal => new
				{
					proposal.BiddingId,
					Proposal = new JobUserBidProposalResult
					{
						BidProposalId = proposal.Id,
						IsBaseBid = proposal.IsBaseBid == true,
						Total = proposal.Total,
						DeliveryDateUtc = proposal.DeliveryDateUtc,
						DeliveryType = proposal.DeliveryType == null ? null : proposal.DeliveryType.Name
					}
				})
				.ToListAsync(cancellationToken);

			foreach (var item in paginatedResponse.Items)
			{
				item.BidProposals = bidProposalLookup
					.Where(proposal => proposal.BiddingId == item.BidId)
					.Select(proposal => proposal.Proposal)
					.ToList();
				item.BidProposalIds = item.BidProposals.Select(proposal => proposal.BidProposalId).ToList();
				item.CanAccept &= item.BidProposals.Any(proposal => proposal.Total.HasValue);
				// An awarded legacy bid with unknown history must not silently select its base proposal.
				item.BidProposalId = item.AcceptedBidProposalId ?? (item.CanAccept
					? item.BidProposals.FirstOrDefault(proposal => proposal.Total.HasValue)?.BidProposalId
					: null);
				var user = await UserRequest.GetUserAsync(item!.CourierId, messageBus);
				item.CourierName = user?.UserName!;
			}
			return paginatedResponse;
		}
	}
}
