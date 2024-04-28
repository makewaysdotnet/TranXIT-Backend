using Carter;
using CourierJobService.Database;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Biddings.CreateBid;

public class CreateBidEndpoint : CarterModule
{
	public CreateBidEndpoint()
		: base("/courierjobservice")
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
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<CreateBidResult>>((int)HttpStatusCode.OK)
		.Produces<Result<CreateBidResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class CreateBid
{
	#region Command
	public class Command : IRequest<Result<CreateBidResult>>
	{
		public required int JobId { get; set; }
		public int UserId { get; set; }
		public double TotalAmount { get; set; }
		public bool? IsInsurancePolicy { get; set; }
		public double? PickupCharges { get; set; }
		public double? HandlingCharges { get; set; }
		public double? CustomClearanceCharges { get; set; }
		public IEnumerable<BidChargesCommand> BidCustomCharges { get; set; } = Enumerable.Empty<BidChargesCommand>();
		public IEnumerable<BidProposalCommand> BidProposals { get; set; } = Enumerable.Empty<BidProposalCommand>();
	}
	public class BidChargesCommand
	{
		public string? Name { get; set; }
		public string? Description { get; set; }
		public double? Amount { get; set; }
	}
	public class BidProposalCommand
	{
		public int? DeliveryTypeId { get; set; }
		public bool? IsBaseBid { get; set; }
		public DateTime? DeliveryDate { get; set; }
		public double? Total { get; set; }
		public IEnumerable<BidProposalItemCommand> BidProposalItems { get; set; } = Enumerable.Empty<BidProposalItemCommand>();
	}
	public record BidProposalItemCommand
	{
		public int? JobItemId { get; set; }
		public double? UnitPrice { get; set; }
		public double? ItemTotal { get; set; }

	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.JobId)
				.NotEmpty().WithMessage("Your Job Id cannot be empty")
				.NotNull().WithMessage("Your Job Id cannot be null");
		}
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Command, Result<CreateBidResult>>
	{
		public async Task<Result<CreateBidResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			request.UserId = HttpContextUser.GetCurrentUserId(httpContext);
			var createdBid = await jobDbContext.Biddings
				.FirstOrDefaultAsync(x => x.JobId == request.JobId && x.UserId == request.UserId,
				cancellationToken);
			if (createdBid is not null)
			{
				return new Error("Bid already exists");
			}
			createdBid = new Bidding
			{
				JobId = request.JobId,
				UserId = request.UserId,
				IsInsurancePolicy = request.IsInsurancePolicy,
				PickupCharges = request.PickupCharges,
				HandlingCharges = request.HandlingCharges,
				CustomClearanceCharges = request.CustomClearanceCharges,
				TotalAmount = request.TotalAmount,
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

			await jobDbContext.Biddings.AddAsync(createdBid);
			await jobDbContext.SaveChangesAsync();
			return new CreateBidResult { BidId = createdBid.Id };
		}
	}
}