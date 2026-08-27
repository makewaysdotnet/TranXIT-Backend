using Carter;
using CourierJobService.Database;
using CourierJobService.Enums;
using CourierJobService.Features.Bids.Shared;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.Data.SqlClient;
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
	public const string AwardConflictError = "Job or bid is no longer eligible for award";

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
		IValidator<Command> validator,
		ILogger<Handler> logger)
		: IRequestHandler<Command, Result<CreateUpdateBidResult>>
	{
		public async Task<Result<CreateUpdateBidResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request, cancellationToken);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var bid = await jobDbContext.Biddings
				.AsNoTracking()
				.Include(x => x.BiddingProposals)
				.FirstOrDefaultAsync(x => x.Id == request.BidId, cancellationToken);
			if (bid is null)
			{
				return new Error("Bid not found");
			}
			var job = await jobDbContext.Jobs.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == bid.JobId, cancellationToken);
			if (job is null)
			{
				return new Error("Bid's Job not found");
			}
			var isCustomer = string.Equals(request.CurrentUserRole, "Customer", StringComparison.OrdinalIgnoreCase);
			var isCourier = string.Equals(request.CurrentUserRole, "Courier", StringComparison.OrdinalIgnoreCase);

			if (request.Status.Equals(JobStatusEnum.Won))
			{
				if (!isCustomer || job.UserId != request.CurrentUserId)
				{
					return new Error(ForbiddenError);
				}

				var selectedBidProposal = bid.BiddingProposals.SingleOrDefault(x => x.Id == request.BidProposalId);
				if (selectedBidProposal is null)
				{
					return new Error("Bid proposal not found");
				}
				if (IsAcceptedProposal(job, bid, request.BidProposalId))
				{
					return new CreateUpdateBidResult { BidId = bid.Id };
				}
				if (selectedBidProposal.Total is null)
				{
					return new Error("Bid proposal total not found");
				}

				return await AwardAsync(request, bid.JobId, cancellationToken);
			}
			else if (request.Status.Equals(JobStatusEnum.InTransit))
			{
				if (!isCourier || bid.UserId != request.CurrentUserId || bid.JobStatusId != (int)JobStatusEnum.Won)
				{
					return new Error(ForbiddenError);
				}

				jobDbContext.Biddings.Attach(bid);
				bid.JobStatusId = (int)JobStatusEnum.InTransit;
				await jobDbContext.SaveChangesAsync(cancellationToken);
			}
			else if (request.Status.Equals(JobStatusEnum.Delivered))
			{
				if (!isCourier || bid.UserId != request.CurrentUserId || bid.JobStatusId != (int)JobStatusEnum.InTransit)
				{
					return new Error(ForbiddenError);
				}

				jobDbContext.Biddings.Attach(bid);
				bid.JobStatusId = (int)JobStatusEnum.Delivered;
				await jobDbContext.SaveChangesAsync(cancellationToken);
			}
			else
			{
				return new Error("Unsupported status transition");
			}
			return new CreateUpdateBidResult { BidId = bid.Id };
		}

		private async Task<Result<CreateUpdateBidResult>> AwardAsync(
			Command request, int jobId, CancellationToken cancellationToken)
		{
			try
			{
				// Claim the job before touching any bid. Disposal rolls back the claim if saving fails.
				await using var transaction = await jobDbContext.Database.BeginTransactionAsync(cancellationToken);
				var claimed = await jobDbContext.Jobs
					.Where(job => job.Id == jobId && job.UserId == request.CurrentUserId &&
						job.AcceptedBidProposalId == null && job.IsJobStatusFromBid != true &&
						(job.JobStatusId == (int)JobStatusEnum.Open || job.JobStatusId == (int)JobStatusEnum.Bidding) &&
						job.ExpiryDateUtc > DateTime.UtcNow &&
						!job.Biddings.Any(other => other.JobStatusId == (int)JobStatusEnum.Won ||
							other.JobStatusId == (int)JobStatusEnum.InTransit || other.JobStatusId == (int)JobStatusEnum.Delivered) &&
						job.Biddings.Any(bid => bid.Id == request.BidId &&
							(bid.JobStatusId == null || bid.JobStatusId == (int)JobStatusEnum.Open ||
							 bid.JobStatusId == (int)JobStatusEnum.Bidding) &&
							bid.BiddingProposals.Any(proposal => proposal.Id == request.BidProposalId && proposal.Total != null)))
					.ExecuteUpdateAsync(setters => setters
						.SetProperty(job => job.AcceptedBidProposalId, request.BidProposalId)
						.SetProperty(job => job.IsJobStatusFromBid, true)
						.SetProperty(job => job.JobStatusId, (int?)null), cancellationToken);

				if (claimed == 0)
				{
					await transaction.RollbackAsync(cancellationToken);
					var currentJob = await jobDbContext.Jobs.AsNoTracking()
						.FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
					var currentBid = await jobDbContext.Biddings.AsNoTracking()
						.FirstOrDefaultAsync(bid => bid.Id == request.BidId, cancellationToken);
					if (currentJob is not null && currentBid is not null &&
						currentJob.UserId == request.CurrentUserId &&
						IsAcceptedProposal(currentJob, currentBid, request.BidProposalId))
					{
						return new CreateUpdateBidResult { BidId = request.BidId };
					}
					return new Error(AwardConflictError);
				}

				var winningBid = await jobDbContext.Biddings
					.Include(bid => bid.BiddingProposals)
					.SingleAsync(bid => bid.Id == request.BidId, cancellationToken);
				var selectedProposal = winningBid.BiddingProposals
					.SingleOrDefault(proposal => proposal.Id == request.BidProposalId);
				if (selectedProposal?.Total is null)
				{
					return new Error("Bid proposal total not found");
				}

				winningBid.JobStatusId = (int)JobStatusEnum.Won;
				// Preserve the existing first-award pricing contract; retries never recalculate it.
				winningBid.TotalAmount = selectedProposal.Total.Value;
				var losingBids = await jobDbContext.Biddings
					.Where(bid => bid.JobId == jobId && bid.Id != winningBid.Id)
					.ToListAsync(cancellationToken);
				losingBids.ForEach(bid => bid.JobStatusId = (int)JobStatusEnum.Lost);

				await jobDbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return new CreateUpdateBidResult { BidId = winningBid.Id };
			}
			catch (Exception exception) when (exception is DbUpdateException or SqlException)
			{
				jobDbContext.ChangeTracker.Clear();
				logger.LogWarning(exception, "Award failed for job {JobId}, bid {BidId}", jobId, request.BidId);
				return new Error("Unable to accept bid. Reload the job and try again.");
			}
		}

		private static bool IsAcceptedProposal(Job job, Bidding bid, int proposalId)
			=> job.IsJobStatusFromBid == true && job.AcceptedBidProposalId == proposalId &&
				bid.JobId == job.Id &&
				(bid.JobStatusId == (int)JobStatusEnum.Won || bid.JobStatusId == (int)JobStatusEnum.InTransit ||
				 bid.JobStatusId == (int)JobStatusEnum.Delivered);
	}
}
