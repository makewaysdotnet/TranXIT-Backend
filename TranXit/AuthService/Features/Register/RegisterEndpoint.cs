using AuthService.Database;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;

namespace AuthService.Features.Register
{
    public static class UserRegister
	{
		public class Command : IRequest<Result<bool>>
		{
			public string Email { get; set; } = string.Empty;
			public string Password { get; set; } = string.Empty;
			public string ConfirmPassword { get; set; } = string.Empty;
			public string Username { get; set; } = string.Empty;
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


		internal sealed class Handler(AuthDbContext authDbContext, IValidator<Command> validator)
			: IRequestHandler<Command, Result<bool>>
		{
			public async Task<Result<bool>> Handle(Command request, CancellationToken cancellationToken)
			{
				var validationResult = await validator.ValidateAsync(request);
				if (!validationResult.IsValid)
				{
					return new Error(validationResult.ToString());
				}
				var user = await authDbContext
					.Users
					.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);
				if (user is not null)
				{
					return new Error("User already exist");
				}
				var passwordHash = BC.EnhancedHashPassword(request.Password);
				var newUser = new User
				{
					Email = request.Email,
					PasswordHash = passwordHash,
					RoleId = request.RoleId,
					Username = request.Username
				};
				await authDbContext.AddAsync(newUser);
				await authDbContext.SaveChangesAsync(cancellationToken);
				return true;
			}
		}
	}

	public class RegisterEndpoint : CarterModule
	{
		public RegisterEndpoint()
			: base("/api")
		{

		}
		public override void AddRoutes(IEndpointRouteBuilder app)
		{
			app.MapPost("/register", async (RegisterRequest request, ISender sender) =>
			{
				var command = request.Adapt<UserRegister.Command>();
				var result = await sender.Send(command);
				if (!result.isSuccess)
				{
					return Results.BadRequest(result);
				}
				return Results.Ok(result);
			})
				.WithName("register")
				.WithOpenApi();
		}
	}

}
