using Carter;
using Microsoft.Extensions.Options;

namespace AccountService.Features.Authentication.ExternalLogins.Google;

public sealed class GoogleLoginEndpoint : CarterModule
{
	public GoogleLoginEndpoint()
		: base("/api")
	{ }

	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/login/google", (IOptions<GoogleExternalLoginOptions> options) =>
		{
			if (!options.Value.Enabled)
			{
				return Results.NotFound();
			}

			return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
		})
		.WithTags("Auth")
		.ExcludeFromDescription();
	}
}
