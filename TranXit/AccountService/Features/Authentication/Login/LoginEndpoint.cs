using AccountService.Database;
using AccountService.Features.Authentication.CommonResults;
using AccountService.Features.Authentication.Refresh;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Authentication.Login;

public class LoginEndpoint : CarterModule
{
	public LoginEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/login", async (LoginRequest request, ISender sender) =>
		{
			var command = request.Adapt<AccountLogin.Command>();
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

public class AccountLogin
{
	public class Command : IRequest<Result<LoginResult>>
	{
		public string Email { get; set; } = string.Empty;

		public string Password { get; set; } = string.Empty;
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Email)
				.NotEmpty().WithMessage("Your email cannot be empty")
				.EmailAddress().WithMessage("Invalid Email Address");

			RuleFor(c => c.Password)
				.NotEmpty().WithMessage("Your password cannot be empty");
			// Creation/reset own password policy. Login verifies the stored credential,
			// including valid passwords issued under a previous policy.
		}
	}
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IJwtTokenBuilder jwtTokenBuilder,
		IRefreshTokenService refreshTokenService,
		IConfiguration configuration)
		: IRequestHandler<Command, Result<LoginResult>>
	{
		public async Task<Result<LoginResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString()); //Error("Validation Error").WithError(validationResult.ToString());
			}
			// Keep credential verification and refresh issuance in the same user lock.
			// Otherwise a login that read the old password can issue a new session after reset.
			await using var transaction = await accountDbContext.Database.BeginTransactionAsync(cancellationToken);
			var normalizedEmail = EmailIdentity.Normalize(request.Email);
			var user = await accountDbContext
				.Users
				.FromSqlInterpolated($"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] = {normalizedEmail}")
				.Include(x => x.Role)
				.SingleOrDefaultAsync(cancellationToken);
			if (user is null)
			{
				return new Error("User doesn't exist");
			}
			if (!BC.EnhancedVerify(request.Password, user.PasswordHash))
			{
				return new Error("Invalid password");
			}
			if (user.IsEmailVerified is not true)
			{
				return new Error(
					"Account verification required",
					new LoginResult
					{
						Id = user.Id,
						Email = user.Email,
						Name = user.Username,
						RoleId = user.RoleId,
						Role = user.Role?.Name,
						IsEmailVerified = false
					});
			}
			var tokenBuilderRequest = new TokenBuilderRequest
			{
				Email = user.Email,
				ExpiryMinutes = double.Parse(configuration["Jwt:ExpiryMinutes"]!),
				Role = user.Role is not null ? user.Role.Name! : "",
				SecretKey = configuration["SharedJwtSecrets:Key"]!,
				Issuer = configuration["Jwt:Issuer"]!,
				Audience = configuration["Jwt:Audience"]!,
				UserId = user!.Id.ToString(),
				Username = user.Username,
				EmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
			};
			var token = jwtTokenBuilder.BuildToken(tokenBuilderRequest);
			var refreshToken = await refreshTokenService.IssueAsync(user, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = user.RoleId,
				Role = user.Role is not null ? user.Role.Name! : null,
				IsEmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
				Token = token,
				RefreshToken = refreshToken.Token,
				RefreshTokenExpires = refreshToken.ExpiresAtUtc.ToString("O"),
				Expires = DateTime.UtcNow.AddMinutes(tokenBuilderRequest.ExpiryMinutes).ToString("O"),
			};
		}
	}
}
