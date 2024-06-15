using AccountService.Database;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.EmailService;
using SharedServicesManager.Helpers;
using System.Net;

namespace AccountService.Features.Authentication.AccountVerification.SendCode;

public class SendCodeEndpoint : CarterModule
{
	public SendCodeEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/send-code", async (SendCodeRequest request, ISender sender) =>
		{
			var command = request.Adapt<SendCode.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<bool>>((int)HttpStatusCode.OK)
		.Produces<Result<bool>>((int)HttpStatusCode.BadRequest);
	}
}
public class SendCode
{
	public class Command : IRequest<Result<bool>>
	{
		public required string Email { get; set; }
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Email)
				.NotEmpty().WithMessage("Your email cannot be empty")
				.EmailAddress().WithMessage("Invalid Email Address");
		}
	}
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IUtils utils,
		IMailService mailService)
		: IRequestHandler<Command, Result<bool>>
	{
		public async Task<Result<bool>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			var user = await accountDbContext
				.Users
				.FirstOrDefaultAsync(x => x.Email == request.Email);
			if (user is null)
			{
				return new Error("User doesn't exist");
			}

			var code = utils.Generate6DRandomCode();
			var mailRequest = new MailRequest
			{
				EmailTo = [user.Email],
				EmailSubject = "Verification Code",
				EmailBody = $"{code}"
			};
			var IsEmailSent = await mailService.SendMail(mailRequest);
			if (!IsEmailSent)
			{
				return new Error("Failed to Send Email");
			}
			user.CodeSentAtUtc = DateTime.UtcNow;
			user.VerificationCode = code;

			accountDbContext.Users.Update(user);
			await accountDbContext.SaveChangesAsync(cancellationToken);
			return true;
		}
	}
}