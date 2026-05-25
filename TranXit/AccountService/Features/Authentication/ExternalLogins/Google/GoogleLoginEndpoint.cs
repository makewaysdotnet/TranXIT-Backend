using AccountService.Database;
using AccountService.Features.Authentication;
using AccountService.Features.Authentication.CommonResults;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

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
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<LoginResult>>((int)HttpStatusCode.OK)
		.Produces<Result<LoginResult>>((int)HttpStatusCode.BadRequest);
	}
}

public class AccountGoogleLogin
{
	public class Command : IRequest<Result<LoginResult>>
	{
		public string Name { get; set; } = string.Empty;
		public string Email { get; set; } = string.Empty;
		public string Image { get; set; } = string.Empty;
		public string? Role { get; set; }
		public int? RoleId { get; set; }
		public string Phone { get; set; } = string.Empty;
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
				return new Error(validationResult.ToString());
			}
			var user = await accountDbContext
				.Users
				.Include(x => x.Role)
				.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
			var hasRoleSelection = PublicRegistrationRoles.HasRoleSelection(request.Role, request.RoleId);
			Role? selectedPublicRole = null;
			if (hasRoleSelection)
			{
				var roleResult = await PublicRegistrationRoles.ResolveAsync(
					accountDbContext,
					request.Role,
					request.RoleId,
					cancellationToken);
				if (roleResult.Error is not null || roleResult.Role is null)
				{
					return new Error(roleResult.Error ?? "Role is invalid");
				}
				selectedPublicRole = roleResult.Role;
			}

			if (user is null)
			{
				if (selectedPublicRole is null)
				{
					return new Error("Role is required");
				}

				//Create User
				user = new User
				{
					Email = request.Email,
					Username = request.Name,
					Provider = request.Provider.ToString(),
					IsEmailVerified = true,
					RoleId = selectedPublicRole.Id,
					Phone = request.Phone
				};
				await accountDbContext.Users.AddAsync(user);
				await accountDbContext.SaveChangesAsync(cancellationToken);
			}
			else if (selectedPublicRole is not null)
			{
				user.RoleId = selectedPublicRole.Id;
				user.Phone = request.Phone;

				accountDbContext.Users.Update(user);
				await accountDbContext.SaveChangesAsync(cancellationToken);
			}
			else if (user.RoleId is null)
			{
				return new Error("Role is required");
			}

			var effectiveRole = selectedPublicRole ??
				user.Role ??
				await accountDbContext.Roles.SingleOrDefaultAsync(
					role => role.Id == user.RoleId,
					cancellationToken);
			if (!PublicRegistrationRoles.IsPublicRole(effectiveRole))
			{
				return new Error("Public Google login supports Customer or Courier only");
			}

			var tokenBuilderRequest = new TokenBuilderRequest
			{
				Email = user.Email,
				ExpiryMinutes = double.Parse(configuration["Jwt:ExpiryMinutes"]!),
				Role = effectiveRole!.Name,
				SecretKey = configuration["SharedJwtSecrets:Key"]!,
				Issuer = configuration["Jwt:Issuer"]!,
				Audience = configuration["Jwt:Audience"]!,
				UserId = user!.Id.ToString(),
				Username = user.Username,
				EmailVerified = true
			};
			var token = jwtTokenBuilder.BuildToken(tokenBuilderRequest);
			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = effectiveRole.Id,
				Role = effectiveRole.Name,
				Provider = Enum.TryParse<ExternalLoginProviderEnum>(user.Provider, out var provider) ?
					provider :
					request.Provider,
				IsEmailVerified = user.IsEmailVerified is null ? false : (bool)user.IsEmailVerified!,
				Expires = DateTime.UtcNow.AddMinutes(tokenBuilderRequest.ExpiryMinutes).ToString(),
				Token = token
			};
		}
	}

}
