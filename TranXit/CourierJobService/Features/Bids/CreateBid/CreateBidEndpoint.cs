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
				return PickupCharges +
					HandlingCharges +
					CustomClearanceCharges +
					BidCustomCharges?.Sum(x => x.Amount) +
					BidProposals.Single(x => x.IsBaseBid == true)?.Total;
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
				.Must(x => x.Count() > 0).WithMessage("There must be atleast one job proposal");
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
			var job = await jobDbContext.Jobs.FindAsync(request.JobId);
			var remainingTime = JobsHelper.GetJobRemainingTime(job!.ExpiryDateUtc, DateTime.Now);

			if (remainingTime is 0)
			{
				return new Error("Job Expired");
			}

			if (remainingTime > 0 && (job.JobStatusId is null || job.JobStatusId == (int)JobStatusEnum.Open))
			{
				job.JobStatusId = (int)JobStatusEnum.Bidding;
				jobDbContext.Jobs.Update(job);
				await jobDbContext.SaveChangesAsync();
			}

			// create new bid
			request.UserId = HttpContextUser.GetCurrentUserId(httpContext);
			var createdBid = await jobDbContext.Biddings
				.Include(x => x.BiddingCharges)
				.Include(x => x.BiddingProposals)
					.ThenInclude(y => y.BiddingProposalItems)
				.AsSplitQuery()
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

			await jobDbContext.Biddings.AddAsync(createdBid);
			await jobDbContext.SaveChangesAsync();
			return new CreateUpdateBidResult { BidId = createdBid.Id };
		}
	}
}