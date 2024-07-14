using AccountService.Database;
using AccountService.Features.Authentication.CommonResults;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.EmailService;
using SharedServicesManager.Helpers;
using System.Net;

namespace AccountService.Features.Authentication.Register;

public class RegisterEndpoint : CarterModule
{
	public RegisterEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/register", async (RegisterRequest request, ISender sender) =>
		{
			var command = request.Adapt<AccountRegister.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<LoginResult>>((int)HttpStatusCode.OK)
		.Produces<Result<LoginResult>>((int)HttpStatusCode.BadRequest);
	}
}

public class AccountRegister
{
	public class Command : IRequest<Result<LoginResult>>
	{
		public string Email { get; set; } = string.Empty;
		public string Password { get; set; } = string.Empty;
		public string ConfirmPassword { get; set; } = string.Empty;
		public string Username { get; set; } = string.Empty;
		public string Phone { get; set; } = string.Empty;
		public int? RoleId { get; set; }
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Email)
				.NotEmpty().WithMessage("Your password cannot be empty")
				.EmailAddress().WithMessage("Invalid Email Address");

			RuleFor(c => c.Password)
				.NotEmpty().WithMessage("Your password cannot be empty")
					.MinimumLength(8)
					.WithMessage("Your password length must be at least 8.")
					.Matches(@"[A-Z]+")
					.WithMessage("Your password must contain at least one uppercase letter.")
					.Matches(@"[a-z]+")
					.WithMessage("Your password must contain at least one lowercase letter.")
					.Matches(@"[0-9]+")
					.WithMessage("Your password must contain at least one number.")
					.Matches(@"[\!\?\@\-\*\.]+")
					.WithMessage("Your password must contain at least one (!*?-@.)");
			RuleFor(c => c.ConfirmPassword).Matches(v => v.Password);
			RuleFor(c => c.Username)
				.NotEmpty().WithMessage("Your username cannot be empty");
		}
	}


	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IUtils utils,
		IMailService mailService)
		: IRequestHandler<Command, Result<LoginResult>>
	{
		public async Task<Result<LoginResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			var user = await accountDbContext
				.Users
				.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
			if (user is not null)
			{
				return new Error("User already exist");
			}
			var passwordHash = BC.EnhancedHashPassword(request.Password);
			user = new User
			{
				Email = request.Email,
				PasswordHash = passwordHash,
				RoleId = request.RoleId,
				Username = request.Username,
				Phone = request.Phone
			};
			await accountDbContext.AddAsync(user);
			await accountDbContext.SaveChangesAsync(cancellationToken);

			//Send Email Verification Code
			//var code = utils.Generate6DRandomCode();
			//var mailRequest = new MailRequest
			//{
			//	EmailTo = [request.Email],
			//	EmailSubject = "Email Verification",
			//	EmailBody = $"{code}"
			//};
			//var isMailSent = await mailService.SendMail(mailRequest);
			//if (!isMailSent)
			//{
			//	return new Error("User Registered Successfully But Email Sent Failed, Retry Verification");
			//}
			//user.CodeSentAtUtc = DateTime.UtcNow;
			//user.VerificationCode = code;

			//accountDbContext.Users.Update(user);
			//await accountDbContext.SaveChangesAsync(cancellationToken);


			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = user.RoleId,
				Role = user.Role is not null ? user.Role.Name! : null,
				IsEmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
			};
		}
	}
}