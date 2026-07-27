using AccountService.Features.Authentication.CommonResults;
using AccountService.Features.Authentication.TokenManager;
using Carter;
using MediatR;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Authentication.Refresh;

public sealed class RefreshEndpoint : CarterModule
{
	public RefreshEndpoint()
		: base("/api")
	{ }

	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/refresh", async (HttpContext httpContext, ISender sender) =>
		{
			var refreshToken = httpContext.Request.Cookies["tranxit_refresh"];
			var result = await sender.Send(new RefreshAccessToken.Command(refreshToken));
			if (!result.isSuccess)
			{
				return Results.Json(result, statusCode: (int)HttpStatusCode.Unauthorized);
			}

			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<LoginResult>>((int)HttpStatusCode.OK)
		.Produces<Result<LoginResult>>((int)HttpStatusCode.Unauthorized);
	}
}

public static class RefreshAccessToken
{
	public sealed record Command(string? RefreshToken) : IRequest<Result<LoginResult>>;

	internal sealed class Handler(
		IRefreshTokenService refreshTokenService,
		IJwtTokenBuilder jwtTokenBuilder,
		IConfiguration configuration)
		: IRequestHandler<Command, Result<LoginResult>>
	{
		public async Task<Result<LoginResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(request.RefreshToken))
			{
				return new Error("Refresh token is required");
			}

			var issue = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken);
			if (issue is null)
			{
				return new Error("Refresh token is invalid or expired");
			}

			var user = issue.User;
			if (user.IsEmailVerified is not true)
			{
				return new Error("Refresh token is invalid or expired");
			}
			var tokenBuilderRequest = new TokenBuilderRequest
			{
				Email = user.Email,
				ExpiryMinutes = double.Parse(configuration["Jwt:ExpiryMinutes"]!),
				Role = user.Role?.Name ?? string.Empty,
				SecretKey = configuration["SharedJwtSecrets:Key"]!,
				Issuer = configuration["Jwt:Issuer"]!,
				Audience = configuration["Jwt:Audience"]!,
				UserId = user.Id.ToString(),
				Username = user.Username,
				EmailVerified = user.IsEmailVerified is true
			};
			var token = jwtTokenBuilder.BuildToken(tokenBuilderRequest);

			return new LoginResult
			{
				Id = user.Id,
				Email = user.Email,
				Name = user.Username,
				RoleId = user.RoleId,
				Role = user.Role?.Name,
				IsEmailVerified = user.IsEmailVerified is true,
				Token = token,
				RefreshToken = issue.Token,
				RefreshTokenExpires = issue.ExpiresAtUtc.ToString("O"),
				Expires = DateTime.UtcNow.AddMinutes(tokenBuilderRequest.ExpiryMinutes).ToString("O")
			};
		}
	}
}
