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
			HttpContext httpContext,
			IRefreshTokenService refreshTokenService,
			CancellationToken cancellationToken) =>
		{
			var refreshToken = httpContext.Request.Cookies["tranxit_refresh"];
			await refreshTokenService.RevokeFamilyAsync(
				refreshToken,
				cancellationToken);

			Result<bool> result = true;
			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Auth")
		.Produces<Result<bool>>((int)HttpStatusCode.OK);
	}
}
