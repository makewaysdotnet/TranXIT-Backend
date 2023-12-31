using AccountService.Database;
using AccountService.Features.Authentication.CommonResults;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;

namespace AccountService.Features.Authentication.ExternalLogins.Google;

public class GoogleLoginEndpoint : CarterModule
{
	public GoogleLoginEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/login/google", async (GoogleLoginRequest request, ISender sender) =>
		{
			var command = request.Adapt<AccountGoogleLogin.Command>();
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		});
	}
}

public class AccountGoogleLogin
{
	public class Command : IRequest<Result<LoginResult>>
	{
		public string Name { get; set; } = string.Empty;
		public string Email { get; set; } = string.Empty;
		public string Image { get; set; } = string.Empty;
		public DateTime? Expires { get; set; } = null;
		public ExternalLoginProviderEnum Provider { get; set; }
	}
	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Email)
				.NotEmpty().WithMessage("Your email can't be empty")
				.EmailAddress().WithMessage("Invalid Email Address");

			RuleFor(c => c.Name)
				.NotEmpty().WithMessage("Your name can't be empty")
				.NotNull().WithMessage("Your name can't be null");

			RuleFor(c => c.Provider)
				.NotNull().WithMessage("Provider can't be null");
		}
	}

	internal sealed class Handler(AccountDbContext authDbContext,
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
			var user = await authDbContext
				.Users
				.Include(x => x.Role)
				.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
			if (user is null)
			{
				//Create User
				user = new User
				{
					Email = request.Email,
					Username = request.Name,
					Provider = request.Provider.ToString(),
					IsEmailVerified = true
				};
				await authDbContext.Users.AddAsync(user);
				await authDbContext.SaveChangesAsync();
			}
			var tokenBuilderRequest = new TokenBuilderRequest
			{
				Email = user.Email,
				ExpiryMinutes = double.Parse(configuration["Jwt:ExpiryMinutes"]!),
				Role = user.Role is not null ? user.Role.Name! : "",
				SecretKey = configuration["JwtSecrets:Key"]!,
				UserId = user!.Id.ToString(),
				Username = user.Username
			};
			var token = jwtTokenBuilder.BuildToken(tokenBuilderRequest);
			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				Role = user.Role is not null ? user.Role.Name! : null,
				Provider = (ExternalLoginProviderEnum)Enum.Parse(typeof(ExternalLoginProviderEnum), user.Provider!),
				Token = token
			};
		}
	}

}
