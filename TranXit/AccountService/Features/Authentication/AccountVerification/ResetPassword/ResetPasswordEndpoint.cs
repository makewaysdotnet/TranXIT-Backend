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
		public required string Email { get; set; }
		public required string Password { get; set; }
		public required string ConfirmPassword { get; set; }
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
		}

		internal sealed class Handler(AccountDbContext accountDbContext,
			IValidator<Command> validator)
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
					.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
				if (user is null)
				{
					return new Error("User doesn't exist");
				}

				var passwordHash = BC.EnhancedHashPassword(request.Password);

				user.PasswordHash = passwordHash;

				accountDbContext.Users.Update(user);
				return await accountDbContext.SaveChangesAsync() > 0;
			}
		}
	}
}