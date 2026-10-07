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

namespace AccountService.Features.Authentication.AccountVerification.ResetPassword;

public class ResetPasswordEndpoint : CarterModule
{
	public ResetPasswordEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/reset-password", async (ResetPasswordRequest request, ISender sender) =>
		{
			var command = request.Adapt<ResetPassword.Command>();
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
public class ResetPassword
{
	public class Command : IRequest<Result<bool>>
	{
		public string Email { get; set; } = string.Empty;
		public string Code { get; set; } = string.Empty;
		public string Password { get; set; } = string.Empty;
		public string ConfirmPassword { get; set; } = string.Empty;
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Email)
				.NotEmpty().WithMessage("Your email cannot be empty")
				.EmailAddress().WithMessage("Invalid Email Address");

			RuleFor(c => c.Code)
				.NotEmpty().WithMessage("Code cannot be empty")
				.Length(6).WithMessage("Invalid Code")
				.Matches(@"^\d{6}$").WithMessage("Invalid Code");

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
		}

		internal sealed class Handler(AccountDbContext accountDbContext,
			IValidator<Command> validator,
			IConfiguration configuration,
			AccountService.Features.Authentication.Refresh.IRefreshTokenService refreshTokenService)
			: IRequestHandler<Command, Result<bool>>
		{
			public async Task<Result<bool>> Handle(Command request, CancellationToken cancellationToken)
			{
				var validationResult = await validator.ValidateAsync(request);
				if (!validationResult.IsValid)
				{
					return new Error(validationResult.ToString());
				}
				var user = await accountDbContext.Users
					.AsNoTracking()
					.FirstOrDefaultAsync(
						x => x.NormalizedEmail == EmailIdentity.Normalize(request.Email),
						cancellationToken);
				if (user is null)
				{
					return new Error("User doesn't exist");
				}
				if (user.VerificationCode is null || user.CodeSentAtUtc is null)
				{
					return new Error("Invalid Code");
				}
				if (!VerificationCodeHasher.Verify(request.Code, user.VerificationCode))
				{
					return new Error("Invalid Code");
				}
				var expiryTime = int.Parse(configuration["CodeVerification:ExpiryMinutes"]!);
				var expiresAt = user.CodeSentAtUtc.Value.AddMinutes(expiryTime);
				if (DateTime.UtcNow > expiresAt)
				{
					return new Error("Code Expired");
				}

				var passwordHash = BC.EnhancedHashPassword(request.Password);

				// Compare-and-consume the exact code snapshot. Concurrent requests may
				// verify the same hash, but only one can claim it. Password, code and
				// refresh revocation commit together, or all roll back.
				await using var transaction = await accountDbContext.Database.BeginTransactionAsync(cancellationToken);
				var earliestValidSentAt = DateTime.UtcNow.AddMinutes(-expiryTime);
				var claimed = await accountDbContext.Users
					.Where(candidate => candidate.Id == user.Id &&
						candidate.VerificationCode == user.VerificationCode &&
						candidate.CodeSentAtUtc == user.CodeSentAtUtc &&
						candidate.CodeSentAtUtc >= earliestValidSentAt)
					.ExecuteUpdateAsync(updates => updates
						.SetProperty(candidate => candidate.PasswordHash, passwordHash)
						.SetProperty(candidate => candidate.VerificationCode, (string?)null)
						.SetProperty(candidate => candidate.CodeSentAtUtc, (DateTime?)null), cancellationToken);
				if (claimed != 1)
				{
					return new Error("Invalid Code");
				}
				await refreshTokenService.RevokeAllForUserAsync(user.Id, "Password reset", cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return true;
			}
		}
	}
}
