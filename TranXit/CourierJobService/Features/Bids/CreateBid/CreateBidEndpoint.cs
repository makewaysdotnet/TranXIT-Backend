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

namespace CourierJobService.Features.Bids.CreateBid;

public class CreateBidEndpoint : CarterModule
{
	public CreateBidEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/bids", async (CreateBidRequest request, ISender sender) =>
		{
			var command = request.Adapt<CreateBid.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Created("/bids", result);
		}).RequireAuthorization("CourierPolicy")
		.WithTags("Bids")
		.WithOpenApi()
		.Produces<Result<CreateUpdateBidResult>>((int)HttpStatusCode.OK)
		.Produces<Result<CreateUpdateBidResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class CreateBid
{
	#region Command
	public class Command : IRequest<Result<CreateUpdateBidResult>>
	{
		public required int JobId { get; set; }
		public int UserId { get; set; }
		public double? TotalAmount
		{
			get
			{
				var proposals = BidProposals ?? Enumerable.Empty<BidProposalCommand>();
				var baseBidTotal = proposals.FirstOrDefault(x => x.IsBaseBid == true)?.Total;

				return PickupCharges +
					HandlingCharges +
					CustomClearanceCharges +
					(BidCustomCharges?.Sum(x => x.Amount) ?? 0) +
					(baseBidTotal ?? 0);
			}
		}
		public bool? IsInsurancePolicy { get; set; }
		public double PickupCharges { get; set; } = 0;
		public double HandlingCharges { get; set; } = 0;
		public double CustomClearanceCharges { get; set; } = 0;
		public IEnumerable<BidChargesCommand> BidCustomCharges { get; set; } = Enumerable.Empty<BidChargesCommand>();
		public required IEnumerable<BidProposalCommand> BidProposals { get; set; }
	}
	public class BidChargesCommand
	{
		public string? Name { get; set; }
		public string? Description { get; set; }
		public double Amount { get; set; } = 0;
	}
	public class BidProposalCommand
	{
		public int? DeliveryTypeId { get; set; }
		public bool? IsBaseBid { get; set; }
		public DateTime? DeliveryDate { get; set; }
		public double Total { get; set; } = 0;
		public IEnumerable<BidProposalItemCommand> BidProposalItems { get; set; } = Enumerable.Empty<BidProposalItemCommand>();
	}
	public record BidProposalItemCommand
	{
		public int? JobItemId { get; set; }
		public double UnitPrice { get; set; } = 0;
		public double ItemTotal { get; set; } = 0;

	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.JobId)
				.NotEmpty().WithMessage("Your Job Id cannot be empty")
				.NotNull().WithMessage("Your Job Id cannot be null");
			RuleFor(c => c.BidProposals)
				.NotEmpty().WithMessage("Your Job Proposal cannot be empty")
				.NotNull().WithMessage("Your Job Proposal cannot be null")
				.Must(x => x?.Any() == true).WithMessage("There must be atleast one job proposal")
				.Must(x => x?.Any(proposal => proposal.IsBaseBid == true) == true)
				.WithMessage("A base bid proposal is required");
			RuleForEach(c => c.BidCustomCharges).ChildRules(charge =>
			{
				charge.RuleFor(c => c.Name)
					.MaximumLength(50).WithMessage("Charge name cannot exceed 50 characters");
				charge.RuleFor(c => c.Description)
					.MaximumLength(100).WithMessage("Charge description cannot exceed 100 characters");
			});
			RuleForEach(c => c.BidProposals).ChildRules(proposal =>
			{
				proposal.RuleFor(c => c.DeliveryTypeId)
					.NotNull().WithMessage("Delivery type is required")
					.GreaterThan(0).WithMessage("Delivery type is invalid");
				proposal.RuleFor(c => c.DeliveryDate)
					.NotNull().WithMessage("Delivery date is required");
				proposal.RuleForEach(c => c.BidProposalItems).ChildRules(item =>
				{
					item.RuleFor(c => c.JobItemId)
						.NotNull().WithMessage("Job item is required")
						.GreaterThan(0).WithMessage("Job item is invalid");
				});
			});
		}
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Command, Result<CreateUpdateBidResult>>
	{
		public async Task<Result<CreateUpdateBidResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			// update job status
			var job = await jobDbContext.Jobs.FindAsync([request.JobId], cancellationToken);
			if (job is null)
			{
				return new Error("Job not found");
			}

			var now = DateTime.UtcNow;
			if (!JobAccess.IsMarketplaceOpen(job, now))
			{
				return new Error("Job is not open for bidding");
			}

			request.UserId = HttpContextUser.GetCurrentUserId(httpContext);
			var bidExists = await jobDbContext.Biddings
				.AnyAsync(
					bid => bid.JobId == request.JobId && bid.UserId == request.UserId,
					cancellationToken);
			if (bidExists)
			{
				return new Error("Bid already exists");
			}

			var proposalItemIds = request.BidProposals
				.SelectMany(proposal => proposal.BidProposalItems)
				.Select(item => item.JobItemId!.Value)
				.Distinct()
				.ToArray();
			if (proposalItemIds.Length > 0)
			{
				var validItemCount = await jobDbContext.JobItems.CountAsync(
					item => item.JobId == request.JobId && proposalItemIds.Contains(item.Id),
					cancellationToken);
				if (validItemCount != proposalItemIds.Length)
				{
					return new Error("One or more job items do not belong to this job");
				}
			}

			if (job.JobStatusId == (int)JobStatusEnum.Open)
			{
				job.JobStatusId = (int)JobStatusEnum.Bidding;
				jobDbContext.Jobs.Update(job);
			}

			var createdBid = new Bidding
			{
				JobId = request.JobId,
				UserId = request.UserId,
				IsInsurancePolicy = request.IsInsurancePolicy,
				PickupCharges = request.PickupCharges,
				HandlingCharges = request.HandlingCharges,
				CustomClearanceCharges = request.CustomClearanceCharges,
				TotalAmount = request.TotalAmount is not null ? (double)request.TotalAmount :
					request.BidProposals.Min(x => x.Total),
				BiddingCharges = request.BidCustomCharges.Select(x => new BiddingCharge
				{
					Amount = x.Amount,
					Description = x.Description,
					Name = x.Name
				}).ToList(),
				BiddingProposals = request.BidProposals.Select(x => new BiddingProposal
				{
					IsBaseBid = x.IsBaseBid,
					DeliveryTypeId = x.DeliveryTypeId,
					DeliveryDateUtc = x.DeliveryDate!.Value.ToUniversalTime(),
					Total = x.Total,
					BiddingProposalItems = x.BidProposalItems.Select(y => new BiddingProposalItem
					{
						ItemTotal = y.ItemTotal,
						JobItemId = y.JobItemId,
						UnitPrice = y.UnitPrice
					}).ToList()
				}).ToList()
			};

			await jobDbContext.Biddings.AddAsync(createdBid, cancellationToken);
			await jobDbContext.SaveChangesAsync(cancellationToken);
			return new CreateUpdateBidResult { BidId = createdBid.Id };
		}
	}
}
