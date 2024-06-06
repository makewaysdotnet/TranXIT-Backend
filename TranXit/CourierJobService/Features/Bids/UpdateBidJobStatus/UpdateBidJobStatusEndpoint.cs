using Carter;
using CourierJobService.Database;
using CourierJobService.Enums;
using CourierJobService.Features.Bids.Shared;
using CourierJobService.Helpers;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Bids.UpdateBidJobStatus;

public class UpdateBidJobStatusEndpoint : CarterModule
{
	public UpdateBidJobStatusEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPut("/bids/status", async (UpdateBidJobStatusRequest request,
			ISender sender) =>
		{
			var command = request.Adapt<UpdateBidJobStatus.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization("CustomerCourierPolicy")
		.WithTags("Bids")
		.WithOpenApi()
		.Produces<Result<CreateUpdateBidResult>>((int)HttpStatusCode.OK)
		.Produces<Result<CreateUpdateBidResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class UpdateBidJobStatus
{
	#region Command
	public class Command : IRequest<Result<CreateUpdateBidResult>>
	{
		public required int BidId { get; set; }
		public required int BidProposalId { get; set; }
		public required JobStatusEnum Status { get; set; }
	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.BidId)
				.NotEmpty().WithMessage("Your Cargo Mode Id cannot be empty")
				.NotNull().WithMessage("Your Cargo Mode Id cannot be null");
			RuleFor(c => c.Status)
				.IsInEnum();
		}
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator)
		: IRequestHandler<Command, Result<CreateUpdateBidResult>>
	{
		public async Task<Result<CreateUpdateBidResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var bid = await jobDbContext.Biddings
				.FindAsync(request.BidId, cancellationToken);
			if (bid is null)
			{
				return new Error("Bid not found");
			}
			var job = await jobDbContext.Jobs.FindAsync(bid.JobId);
			if (job is null)
			{
				return new Error("Bid's Job not found");
			}
			var remainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, DateTime.UtcNow);

			if (remainingTime > 0 && request.Status.Equals(JobStatusEnum.Won))
			{
				// update job status and bit
				job.JobStatusId = null;
				job.IsJobStatusFromBid = true;
				jobDbContext.Jobs.Update(job);
				await jobDbContext.SaveChangesAsync();

				// get all bid proposals 
				var bidProposals = await jobDbContext.BiddingProposals
					.Where(x => x.BiddingId == bid.Id)
					.ToListAsync();

				// update bid status to won total amount to selected proposal
				var selectedBidProposal = bidProposals.Find(x => x.Id == request.BidProposalId);
				bid.JobStatusId = (int)JobStatusEnum.Won;
				bid.TotalAmount = (double)selectedBidProposal!.Total!;
				jobDbContext.Biddings.Update(bid);
				await jobDbContext.SaveChangesAsync();

				// delete remaining bid proposals
				var deleteBidProposals = bidProposals.Where(x => x.Id != request.BidProposalId);
				jobDbContext.BiddingProposals.RemoveRange(deleteBidProposals);
				await jobDbContext.SaveChangesAsync();

				// update remaining bid status to lost
				var lostBids = await jobDbContext.Biddings
					.Where(x => x.JobId == job.Id)
					.ToListAsync();
				lostBids.ForEach(x => x.JobStatusId = (int)JobStatusEnum.Lost);
				jobDbContext.Biddings.UpdateRange(lostBids);
				await jobDbContext.SaveChangesAsync();
			}
			else if (request.Status.Equals(JobStatusEnum.InTransit))
			{
				bid.JobStatusId = (int)JobStatusEnum.InTransit;
				jobDbContext.Biddings.Update(bid);
				await jobDbContext.SaveChangesAsync();
			}
			else if (request.Status.Equals(JobStatusEnum.Delivered))
			{
				bid.JobStatusId = (int)JobStatusEnum.Delivered;
				jobDbContext.Biddings.Update(bid);
				await jobDbContext.SaveChangesAsync();
			}
			return new CreateUpdateBidResult { BidId = bid.Id };
		}
	}
}
