using AccountService.Database;
using AccountService.Features.Authentication.CommonResults;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;

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
		})
			.WithName("Login");
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
				.NotEmpty().WithMessage("Your password cannot be empty")
					.MinimumLength(8).WithMessage("Your password length must be at least 8.")
					.Matches(@"[A-Z]+").WithMessage("Your password must contain at least one uppercase letter.")
					.Matches(@"[a-z]+").WithMessage("Your password must contain at least one lowercase letter.")
					.Matches(@"[0-9]+").WithMessage("Your password must contain at least one number.")
					.Matches(@"[\!\?\@\-\*\.]+").WithMessage("Your password must contain at least one (!*?-@.)");
		}
	}
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator,
		IJwtTokenBuilder jwtTokenBuilder,
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
			var user = await accountDbContext
				.Users
				.Include(x => x.Role)
				.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
			if (user is null)
			{
				return new Error("User doesn't exist");
			}
			if (!BC.EnhancedVerify(request.Password, user.PasswordHash))
			{
				return new Error("Invalid password");
			}
			var tokenBuilderRequest = new TokenBuilderRequest
			{
				Email = user.Email,
				ExpiryMinutes = double.Parse(configuration["Jwt:ExpiryMinutes"]!),
				Role = user.Role is not null ? user.Role.Name! : "",
				SecretKey = configuration["JwtSecrets:Key"]!,
				UserId = user!.Id.ToString(),
				Username = user.Username,
				EmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
			};
			var token = jwtTokenBuilder.BuildToken(tokenBuilderRequest);
			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = user.RoleId,
				Role = user.Role is not null ? user.Role.Name! : null,
				IsEmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
				Token = token,
				Expires = DateTime.UtcNow.AddMinutes(tokenBuilderRequest.ExpiryMinutes).ToString(),
			};
		}
	}
}