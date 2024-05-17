using Carter;
using CourierJobService.Database;
using FluentValidation;
using Mapster;
using MediatR;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.CreateJob;

public class CreateJobEndpoint : CarterModule
{
	public CreateJobEndpoint()
		: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/jobs", async (CreateJobRequest request, ISender sender) =>
		{
			var command = request.Adapt<CreateJob.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Created("/jobs", result);
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<CreateJobResult>>((int)HttpStatusCode.OK)
		.Produces<Result<CreateJobResult>>((int)HttpStatusCode.BadRequest);
	}
}

public class CreateJob
{
	#region Command
	public class Command : IRequest<Result<CreateJobResult>>
	{
		public int CourierModeId { get; set; }
		public int UserId { get; set; }
		public int CargoModeId { get; set; }
		public int? ItemTypeId { get; set; }
		public int? OriginCountryId { get; set; }
		public int? DestinationCountryId { get; set; }
		public int? OriginCityId { get; set; }
		public int? DestinationCityId { get; set; }
		public string? OriginAddress { get; set; }
		public string? DestinationAddress { get; set; }
		public string RecipientContact { get; init; } = string.Empty;
		public string RecipientName { get; init; } = string.Empty;
		public string RecipientEmail { get; init; } = string.Empty;
		public DateTime? PickupDateUtc { get; init; }
		public IEnumerable<JobItemCommand> JobItems { get; set; } = Enumerable.Empty<JobItemCommand>();

	}
	public class JobItemCommand
	{
		public string? ItemName { get; init; }
		public string? ImageUrl { get; init; }
		public string? Dimensions { get; init; }
		public string? Description { get; init; }
		public int? Quantity { get; set; }
		public int? ItemTypeId { get; set; }
		public double? Weight { get; set; }
		public double? DeclaredValue { get; set; }
	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.CargoModeId)
				.NotEmpty().WithMessage("Your Cargo Mode Id cannot be empty")
				.NotNull().WithMessage("Your Cargo Mode Id cannot be null");
			RuleFor(c => c.CourierModeId)
				.NotEmpty().WithMessage("Your Courier Mode Id cannot be empty")
				.NotNull().WithMessage("Your Courier Mode Id cannot be null");
			RuleFor(c => c.RecipientContact)
				.NotEmpty().WithMessage("Your Recipient Contact cannot be empty")
				.NotNull().WithMessage("Your Recipient Contact cannot be null");
		}
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator,
		IHttpContextAccessor httpContext,
		IUtils utils)
		: IRequestHandler<Command, Result<CreateJobResult>>
	{
		public async Task<Result<CreateJobResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			request.UserId = HttpContextUser.GetCurrentUserId(httpContext);

			var createdJob = new Job
			{
				UserId = request.UserId,
				DestinationAddress = request.DestinationAddress,
				OriginAddress = request.OriginAddress,
				CargoModeId = request.CargoModeId,
				CourierModeId = request.CourierModeId,
				CreatedOnUtc = DateTime.UtcNow,
				DestinationCityId = request.DestinationCityId,
				DestinationCountryId = request.DestinationCountryId,
				OriginCountryId = request.OriginCountryId,
				OriginCityId = request.OriginCityId,
				JobNumber = utils.GenerateJobNumber(),
				JobItems = request.JobItems.Select(x => new JobItem
				{
					DeclaredValue = x.DeclaredValue,
					Description = x.Description,
					Name = x.ItemName,
					Quantity = x.Quantity,
					Dimensions = x.Dimensions,
					Weight = x.Weight,
					ImageUrl = x.ImageUrl
				}).ToList(),
			};

			await jobDbContext.Jobs.AddAsync(createdJob);
			await jobDbContext.SaveChangesAsync();
			return new CreateJobResult { JobId = createdJob.Id };
		}
	}
}
