using Carter;
using CourierJobService.Database;
using CourierJobService.Enums;
using CourierJobService.Features.Jobs.Shared;
using CourierJobService.Helpers;
using FluentValidation;
using Mapster;
using MediatR;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Jobs.UpdateJobStatus;

public class UpdateJobStatusEndpoint : CarterModule
{
	public UpdateJobStatusEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPut("/jobs/status", async (UpdateJobStatusRequest request,
			ISender sender) =>
		{
			var command = request.Adapt<UpdateJobStatus.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
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
	#region Command
	public class Command : IRequest<Result<CreateUpdateJobResult>>
	{
		public required int JobId { get; set; }
		public required JobStatusEnum Status { get; set; }
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
			var remainingTime = JobsHelper.GetJobRemainingTime(job!.ExpiryDateUtc, DateTime.UtcNow);

			if (remainingTime is 0 && request.Status.Equals(JobStatusEnum.Closed))
			{
				job.JobStatusId = (int)JobStatusEnum.Closed;
				jobDbContext.Jobs.Update(job);
				await jobDbContext.SaveChangesAsync();
			}
			return new CreateUpdateJobResult { JobId = job.Id };
		}
	}
}
