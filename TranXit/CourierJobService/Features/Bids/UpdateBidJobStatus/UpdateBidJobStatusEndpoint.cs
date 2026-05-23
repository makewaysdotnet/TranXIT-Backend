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
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Bids.UpdateBidJobStatus;

public class UpdateBidJobStatusEndpoint : CarterModule
{
	public UpdateBidJobStatusEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPut("/bids/status", async (UpdateBidJobStatusRequest request,
			ISender sender,
			IHttpContextAccessor httpContext) =>
		{
			var command = request.Adapt<UpdateBidJobStatus.Command>();
			command.CurrentUserId = HttpContextUser.GetCurrentUserId(httpContext);
			command.CurrentUserRole = HttpContextUser.GetCurrentUserRole(httpContext);
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				if (result.error.Contains(UpdateBidJobStatus.ForbiddenError))
				{
					return Results.Forbid();
				}
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
	public const string ForbiddenError = "Forbidden";

	#region Command
	public class Command : IRequest<Result<CreateUpdateBidResult>>
	{
		public required int BidId { get; set; }
		public required int BidProposalId { get; set; }
		public required JobStatusEnum Status { get; set; }
		public int CurrentUserId { get; set; }
		public string CurrentUserRole { get; set; } = string.Empty;
	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.BidId)
				.NotEmpty().WithMessage("Your Bid Id cannot be empty")
				.NotNull().WithMessage("Your Bid Id cannot be null");
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
				.Include(x => x.BiddingProposals)
				.FirstOrDefaultAsync(x => x.Id == request.BidId, cancellationToken);
			if (bid is null)
			{
				return new Error("Bid not found");
			}
			var job = await jobDbContext.Jobs.FindAsync([bid.JobId], cancellationToken);
			if (job is null)
			{
				return new Error("Bid's Job not found");
			}
			var isCustomer = string.Equals(request.CurrentUserRole, "Customer", StringComparison.OrdinalIgnoreCase);
			var isCourier = string.Equals(request.CurrentUserRole, "Courier", StringComparison.OrdinalIgnoreCase);
			var remainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, DateTime.UtcNow);

			if (remainingTime > 0 && request.Status.Equals(JobStatusEnum.Won))
			{
				if (!isCustomer || job.UserId != request.CurrentUserId)
				{
					return new Error(ForbiddenError);
				}

				var selectedBidProposal = bid.BiddingProposals.SingleOrDefault(x => x.Id == request.BidProposalId);
				if (selectedBidProposal?.Total is null)
				{
					return new Error("Bid proposal not found");
				}

				// update job status and bit
				job.JobStatusId = null;
				job.IsJobStatusFromBid = true;
				jobDbContext.Jobs.Update(job);

				// update bid status to won total amount to selected proposal
				bid.JobStatusId = (int)JobStatusEnum.Won;
				bid.TotalAmount = selectedBidProposal.Total.Value;
				jobDbContext.Biddings.Update(bid);

				// delete remaining bid proposals
				var deleteBidProposals = bid.BiddingProposals.Where(x => x.Id != request.BidProposalId);
				jobDbContext.BiddingProposals.RemoveRange(deleteBidProposals);

				// update remaining bid status to lost
				var lostBids = await jobDbContext.Biddings
					.Where(x => x.JobId == job.Id && x.Id != bid.Id)
					.ToListAsync(cancellationToken);
				lostBids.ForEach(x => x.JobStatusId = (int)JobStatusEnum.Lost);
				jobDbContext.Biddings.UpdateRange(lostBids);
				await jobDbContext.SaveChangesAsync(cancellationToken);
			}
			else if (request.Status.Equals(JobStatusEnum.Won))
			{
				return new Error("Job Expired");
			}
			else if (request.Status.Equals(JobStatusEnum.InTransit))
			{
				if (!isCourier || bid.UserId != request.CurrentUserId || bid.JobStatusId != (int)JobStatusEnum.Won)
				{
					return new Error(ForbiddenError);
				}

				bid.JobStatusId = (int)JobStatusEnum.InTransit;
				jobDbContext.Biddings.Update(bid);
				await jobDbContext.SaveChangesAsync(cancellationToken);
			}
			else if (request.Status.Equals(JobStatusEnum.Delivered))
			{
				if (!isCourier || bid.UserId != request.CurrentUserId || bid.JobStatusId != (int)JobStatusEnum.InTransit)
				{
					return new Error(ForbiddenError);
				}

				bid.JobStatusId = (int)JobStatusEnum.Delivered;
				jobDbContext.Biddings.Update(bid);
				await jobDbContext.SaveChangesAsync(cancellationToken);
			}
			else
			{
				return new Error("Unsupported status transition");
			}
			return new CreateUpdateBidResult { BidId = bid.Id };
		}
	}
}
