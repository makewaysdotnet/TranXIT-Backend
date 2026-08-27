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
		public bool? IsInsurancePolicy { get; set; }
		public decimal PickupCharges { get; set; } = 0;
		public decimal HandlingCharges { get; set; } = 0;
		public decimal CustomClearanceCharges { get; set; } = 0;
		public IEnumerable<BidChargesCommand> BidCustomCharges { get; set; } = Enumerable.Empty<BidChargesCommand>();
		public required IEnumerable<BidProposalCommand> BidProposals { get; set; }
	}
	public class BidChargesCommand
	{
		public string? Name { get; set; }
		public string? Description { get; set; }
		public decimal Amount { get; set; } = 0;
	}
	public class BidProposalCommand
	{
		public int? DeliveryTypeId { get; set; }
		public bool? IsBaseBid { get; set; }
		public DateTime? DeliveryDate { get; set; }
		public decimal Total { get; set; } = 0;
		public IEnumerable<BidProposalItemCommand> BidProposalItems { get; set; } = Enumerable.Empty<BidProposalItemCommand>();
	}
	public record BidProposalItemCommand
	{
		public int? JobItemId { get; set; }
		public decimal UnitPrice { get; set; } = 0;
		public decimal ItemTotal { get; set; } = 0;

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
				.Must(x => x?.Count(proposal => proposal?.IsBaseBid == true) == 1)
				.WithMessage("Exactly one base bid proposal is required");
			RuleFor(c => c.PickupCharges).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
			RuleFor(c => c.HandlingCharges).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
			RuleFor(c => c.CustomClearanceCharges).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
			RuleFor(c => c.BidCustomCharges).NotNull();
			RuleForEach(c => c.BidCustomCharges).NotNull().ChildRules(charge =>
			{
				charge.RuleFor(c => c.Amount).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
				charge.RuleFor(c => c.Name)
					.MaximumLength(50).WithMessage("Charge name cannot exceed 50 characters");
				charge.RuleFor(c => c.Description)
					.MaximumLength(100).WithMessage("Charge description cannot exceed 100 characters");
			});
			RuleForEach(c => c.BidProposals).NotNull().ChildRules(proposal =>
			{
				proposal.RuleFor(c => c.Total).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
				proposal.RuleFor(c => c.BidProposalItems).NotNull()
					.Must(items => items is not null && items.Where(item => item is not null)
						.GroupBy(item => item.JobItemId).All(group => group.Count() == 1))
					.WithMessage("Each job item may appear only once per proposal");
				proposal.RuleFor(c => c.DeliveryTypeId)
					.NotNull().WithMessage("Delivery type is required")
					.GreaterThan(0).WithMessage("Delivery type is invalid");
				proposal.RuleFor(c => c.DeliveryDate)
					.NotNull().WithMessage("Delivery date is required");
				proposal.RuleForEach(c => c.BidProposalItems).NotNull().ChildRules(item =>
				{
					item.RuleFor(c => c.UnitPrice).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
					item.RuleFor(c => c.ItemTotal).Must(QuoteAmount.IsValid).WithMessage(QuoteAmount.ValidationMessage);
					item.RuleFor(c => c.JobItemId)
						.NotNull().WithMessage("Job item is required")
						.GreaterThan(0).WithMessage("Job item is invalid");
				});
			});
			RuleFor(c => c).Custom((command, context) =>
			{
				foreach (var proposal in command.BidProposals ?? [])
				{
					if (proposal is null || !TryProposalTotal(command, proposal, out var total) || total != proposal.Total)
					{
						context.AddFailure(nameof(command.BidProposals), "Proposal total must equal item totals plus all shared charges exactly once, within the supported amount limit");
					}
				}
			});
		}
	}

	private static bool TryProposalTotal(Command command, BidProposalCommand proposal, out decimal total)
	{
		total = 0;
		if (command.BidCustomCharges is null || command.BidCustomCharges.Any(charge => charge is null) ||
			proposal.BidProposalItems is null || proposal.BidProposalItems.Any(item => item is null))
		{
			return false;
		}
		return QuoteAmount.TrySum(new[] { command.PickupCharges, command.HandlingCharges, command.CustomClearanceCharges }
			.Concat(command.BidCustomCharges.Select(charge => charge.Amount))
			.Concat(proposal.BidProposalItems.Select(item => item.ItemTotal)), out total);
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Command, Result<CreateUpdateBidResult>>
	{
		public async Task<Result<CreateUpdateBidResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request, cancellationToken);
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
				var items = await jobDbContext.JobItems
					.Where(item => item.JobId == request.JobId && proposalItemIds.Contains(item.Id))
					.ToDictionaryAsync(item => item.Id, cancellationToken);
				if (items.Count != proposalItemIds.Length)
				{
					return new Error("One or more job items do not belong to this job");
				}
				foreach (var line in request.BidProposals.SelectMany(proposal => proposal.BidProposalItems))
				{
					var quantity = items[line.JobItemId!.Value].Quantity;
					if (quantity is null or <= 0 || line.UnitPrice * quantity.Value != line.ItemTotal)
					{
						return new Error("Item total must equal unit price times the shipment item quantity");
					}
				}
			}

			var quotes = new List<(BidProposalCommand Proposal, decimal Total)>();
			foreach (var proposal in request.BidProposals)
			{
				if (!TryProposalTotal(request, proposal, out var total))
				{
					return new Error("Quote total is outside the supported amount limit");
				}
				quotes.Add((proposal, total));
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
				PickupCharges = (double)request.PickupCharges,
				HandlingCharges = (double)request.HandlingCharges,
				CustomClearanceCharges = (double)request.CustomClearanceCharges,
				TotalAmount = (double)quotes.Single(quote => quote.Proposal.IsBaseBid == true).Total,
				BiddingCharges = request.BidCustomCharges.Select(x => new BiddingCharge
				{
					Amount = (double)x.Amount,
					Description = x.Description,
					Name = x.Name
				}).ToList(),
				BiddingProposals = quotes.Select(quote => new BiddingProposal
				{
					IsBaseBid = quote.Proposal.IsBaseBid,
					DeliveryTypeId = quote.Proposal.DeliveryTypeId,
					DeliveryDateUtc = quote.Proposal.DeliveryDate!.Value.ToUniversalTime(),
					Total = (double)quote.Total,
					BiddingProposalItems = quote.Proposal.BidProposalItems.Select(y => new BiddingProposalItem
					{
						ItemTotal = (double)y.ItemTotal,
						JobItemId = y.JobItemId,
						UnitPrice = (double)y.UnitPrice
					}).ToList()
				}).ToList()
			};

			await jobDbContext.Biddings.AddAsync(createdBid, cancellationToken);
			await jobDbContext.SaveChangesAsync(cancellationToken);
			return new CreateUpdateBidResult { BidId = createdBid.Id };
		}
	}
}
