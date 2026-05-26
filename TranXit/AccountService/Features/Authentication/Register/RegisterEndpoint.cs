using AccountService.Database;
using AccountService.Features.Authentication;
using AccountService.Features.Authentication.AccountVerification;
using AccountService.Features.Authentication.CommonResults;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
			if (!result.isSuccess && result.value is null)
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
		public string? Role { get; set; }
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
					.Matches(@"[!@#$%^&*(),.?""{}|<>]+")
					.WithMessage("Your password must contain at least one special character");
			RuleFor(c => c.ConfirmPassword)
				.Equal(c => c.Password).WithMessage("Passwords do not match.");
			RuleFor(c => c.Username)
				.NotEmpty().WithMessage("Your username cannot be empty");
		}
	}


	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IUtils utils,
		IMailService mailService,
		IOptions<MailSettings> mailSettings,
		IHostEnvironment environment)
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

			if (user is not null && Convert.ToBoolean(user.IsEmailVerified))
			{
				return new Error("User already exist");
			}

			if (user is not null && !Convert.ToBoolean(user.IsEmailVerified))
			{
				var data = new LoginResult
				{
					IsEmailVerified = Convert.ToBoolean(user.IsEmailVerified)
				};
				return new Error("User already exist but not verified", data);
			}

			var passwordHash = BC.EnhancedHashPassword(request.Password);
			var roleResult = await PublicRegistrationRoles.ResolveAsync(
				accountDbContext,
				request.Role,
				request.RoleId,
				cancellationToken);
			if (roleResult.Error is not null || roleResult.Role is null)
			{
				return new Error(roleResult.Error ?? "Role is invalid");
			}

			var verificationCode = VerificationCodeHasher.Format(utils.Generate6DRandomCode());

			user = new User
			{
				Email = request.Email,
				PasswordHash = passwordHash,
				RoleId = roleResult.Role.Id,
				Username = request.Username,
				Phone = request.Phone,
				CodeSentAtUtc = DateTime.UtcNow,
				VerificationCode = VerificationCodeHasher.Hash(verificationCode)
			};

			await accountDbContext.AddAsync(user);
			await accountDbContext.SaveChangesAsync(cancellationToken);

			var mailRequest = new MailRequest
			{
				EmailTo = [request.Email],
				EmailSubject = "Email Verification",
				EmailBody = verificationCode
			};
			var isMailSent = await mailService.SendMail(mailRequest, cancellationToken);
			if (!isMailSent)
			{
				return new Error("User Registered Successfully But Email Sent Failed, Retry Verification");
			}

			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = roleResult.Role.Id,
				Role = roleResult.Role.Name,
				IsEmailVerified = Convert.ToBoolean(user.IsEmailVerified),
				DevelopmentVerificationCode = environment.IsDevelopment() &&
					mailSettings.Value.DisableSending ?
					verificationCode :
					null
			};
		}
	}
}
