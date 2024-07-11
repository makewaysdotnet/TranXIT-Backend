using Carter;
using CourierJobService.Database;
using CourierJobService.Features.Jobs.Shared;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;
using JobStatusEnum = CourierJobService.Enums.JobStatusEnum;

namespace CourierJobService.Features.Jobs.CreateJob;

public class CreateJobEndpoint : CarterModule
{
	public CreateJobEndpoint()
		: base("/api")
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
		}).RequireAuthorization("CustomerPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<CreateUpdateJobResult>>((int)HttpStatusCode.Created)
		.Produces<Result<CreateUpdateJobResult>>((int)HttpStatusCode.BadRequest);
	}
}

public class CreateJob
{
	#region Command
	public class Command : IRequest<Result<CreateUpdateJobResult>>
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
		public DateTime? ExpiryDateUtc { get; init; }
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
		IConfiguration configuration,
		IUtils utils)
		: IRequestHandler<Command, Result<CreateUpdateJobResult>>
	{
		public async Task<Result<CreateUpdateJobResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			request.UserId = HttpContextUser.GetCurrentUserId(httpContext);
			var jobStatusId = jobDbContext.JobStatuses
				.AsNoTracking()
				.SingleOrDefault(x => x.Status == JobStatusEnum.Open.ToString())?.Id;
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
				JobStatusId = jobStatusId,
				RecipientContact = request.RecipientContact,
				RecipientEmail = request.RecipientEmail,
				RecipientName = request.RecipientName,
				ExpiryDateUtc = request.ExpiryDateUtc.HasValue ?
					request.ExpiryDateUtc :
					DateTime.UtcNow.AddMinutes(double.Parse(configuration["Jobs:ExpiryTimeInMinutes"]!)),
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
			return new CreateUpdateJobResult { JobId = createdJob.Id };
		}
	}
}
