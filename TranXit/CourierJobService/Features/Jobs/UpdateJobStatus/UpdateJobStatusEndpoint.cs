using Carter;
using CourierJobService.Database;
using CourierJobService.Enums;
using CourierJobService.Features.Jobs.Shared;
using CourierJobService.Helpers;
using FluentValidation;
using Mapster;
using MediatR;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.UpdateJobStatus;

public class UpdateJobStatusEndpoint : CarterModule
{
	public UpdateJobStatusEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPut("/jobs/status", async (UpdateJobStatusRequest request,
			ISender sender,
			IHttpContextAccessor httpContext) =>
		{
			var command = request.Adapt<UpdateJobStatus.Command>();
			command.CurrentUserId = HttpContextUser.GetCurrentUserId(httpContext);
			command.CurrentUserRole = HttpContextUser.GetCurrentUserRole(httpContext);
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				if (result.error.Contains(UpdateJobStatus.ForbiddenError))
				{
					return Results.Forbid();
				}
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization("CustomerCourierPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<CreateUpdateJobResult>>((int)HttpStatusCode.OK)
		.Produces<Result<CreateUpdateJobResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class UpdateJobStatus
{
	public const string ForbiddenError = "Forbidden";

	#region Command
	public class Command : IRequest<Result<CreateUpdateJobResult>>
	{
		public required int JobId { get; set; }
		public required JobStatusEnum Status { get; set; }
		public int CurrentUserId { get; set; }
		public string CurrentUserRole { get; set; } = string.Empty;
	}
	#endregion
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.JobId)
				.NotEmpty().WithMessage("Your Cargo Mode Id cannot be empty")
				.NotNull().WithMessage("Your Cargo Mode Id cannot be null");
			RuleFor(c => c.Status)
				.IsInEnum();
		}
	}
	internal sealed class Handler(CourierJobDbContext jobDbContext,
		IValidator<Command> validator)
		: IRequestHandler<Command, Result<CreateUpdateJobResult>>
	{
		public async Task<Result<CreateUpdateJobResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var job = await jobDbContext.Jobs
				.FindAsync(request.JobId, cancellationToken);
			if (job is null)
			{
				return new Error("Job not found");
			}

			var isOwningCustomer =
				string.Equals(request.CurrentUserRole, "Customer", StringComparison.OrdinalIgnoreCase) &&
				job.UserId == request.CurrentUserId;
			if (!isOwningCustomer)
			{
				return new Error(ForbiddenError);
			}

			if (request.Status != JobStatusEnum.Closed)
			{
				return new Error("Unsupported status transition");
			}

			if (job.IsJobStatusFromBid is true ||
				(job.JobStatusId != (int)JobStatusEnum.Open &&
				 job.JobStatusId != (int)JobStatusEnum.Bidding))
			{
				return new Error("Job cannot be closed from its current status");
			}

			var remainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, DateTime.UtcNow);
			if (remainingTime > 0)
			{
				return new Error("Job has not expired");
			}

			job.JobStatusId = (int)JobStatusEnum.Closed;
			jobDbContext.Jobs.Update(job);
			await jobDbContext.SaveChangesAsync(cancellationToken);
			return new CreateUpdateJobResult { JobId = job.Id };
		}
	}
}
