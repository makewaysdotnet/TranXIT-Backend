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
			var result = false;
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			var user = await accountDbContext
				.Users
				.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
			if (user is null)
			{
				return new Error("User doesn't exist");
			}
			var expiryTime = int.Parse(configuration["CodeVerification:ExpiryMinutes"]!);
			if (!VerificationCodeHasher.Verify(request.Code, user.VerificationCode))
			{
				return new Error("Invalid Code");
			}
			var codeSentAt = user.CodeSentAtUtc?.AddMinutes(expiryTime)!;
			if (DateTime.UtcNow > codeSentAt)
			{
				return new Error("Code Expired");
			}
			user.IsEmailVerified = true;
			user.VerificationCode = null;
			user.CodeSentAtUtc = null;
			accountDbContext.Users.Update(user);
			await accountDbContext.SaveChangesAsync(cancellationToken);
			result = true;
			return result;
		}
	}
}
