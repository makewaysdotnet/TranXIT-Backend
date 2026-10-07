using Carter;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Authentication.Refresh;

public sealed class LogoutEndpoint : CarterModule
{
	public LogoutEndpoint()
		: base("/api")
	{ }

	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/logout", async (
			LogoutRequest? request,
			[Microsoft.AspNetCore.Mvc.FromServices] IRefreshTokenService refreshTokenService,
			CancellationToken cancellationToken) =>
		{
			// Like refresh, this private transport accepts an explicit credential from
			// the BFF, never an ambient browser Cookie header.
			if (string.IsNullOrWhiteSpace(request?.RefreshToken))
			{
				Result<bool> denied = new Error("Refresh token is required");
				return Results.Json(denied, statusCode: (int)HttpStatusCode.Unauthorized);
			}
			await refreshTokenService.RevokeFamilyAsync(
				request.RefreshToken,
				cancellationToken);

			Result<bool> result = true;
			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<bool>>((int)HttpStatusCode.OK)
		.Produces<Result<bool>>((int)HttpStatusCode.Unauthorized);
	}

	public sealed record LogoutRequest(string? RefreshToken);
}
