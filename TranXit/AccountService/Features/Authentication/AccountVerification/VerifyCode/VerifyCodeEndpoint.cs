using AccountService.Database;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Authentication.AccountVerification.VerifyCode;

public class VerifyCodeEndpoint : CarterModule
{
	public VerifyCodeEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/verify-code", async (VerifyCodeRequest request, ISender sender) =>
		{
			var command = request.Adapt<VerifyCode.Command>();
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
public class VerifyCode
{
	public class Command : IRequest<Result<bool>>
	{
		public required string Email { get; set; }
		public required string Code { get; set; }
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
		}
	}
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IConfiguration configuration)
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
				.AsNoTracking()
				.FirstOrDefaultAsync(
					x => x.NormalizedEmail == EmailIdentity.Normalize(request.Email),
					cancellationToken);
			if (user is null)
			{
				return new Error("User doesn't exist");
			}
			var expiryTime = int.Parse(configuration["CodeVerification:ExpiryMinutes"]!);
			if (user.CodeSentAtUtc is null || !VerificationCodeHasher.Verify(request.Code, user.VerificationCode))
			{
				return new Error("Invalid Code");
			}
			var codeSentAt = user.CodeSentAtUtc.Value.AddMinutes(expiryTime);
			if (DateTime.UtcNow > codeSentAt)
			{
				return new Error("Code Expired");
			}
			// Both verification and password reset consume the same OTP state.
			// Claim its exact snapshot once, recheck expiry at the write, and touch
			// only verification fields so a concurrent reset cannot be reverted.
			var earliestValidSentAt = DateTime.UtcNow.AddMinutes(-expiryTime);
			var claimed = await accountDbContext.Users
				.Where(candidate => candidate.Id == user.Id &&
					candidate.PasswordHash == user.PasswordHash &&
					candidate.VerificationCode == user.VerificationCode &&
					candidate.CodeSentAtUtc == user.CodeSentAtUtc &&
					candidate.CodeSentAtUtc >= earliestValidSentAt)
				.ExecuteUpdateAsync(updates => updates
					.SetProperty(candidate => candidate.IsEmailVerified, true)
					.SetProperty(candidate => candidate.VerificationCode, (string?)null)
					.SetProperty(candidate => candidate.CodeSentAtUtc, (DateTime?)null), cancellationToken);
			return claimed == 1 ? true : new Error("Invalid Code");
		}
	}
}
