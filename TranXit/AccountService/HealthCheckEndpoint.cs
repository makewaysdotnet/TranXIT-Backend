using Carter;

namespace AccountService
{
	public class HealthCheckEndpoint : CarterModule
	{
		public HealthCheckEndpoint() : base("/")
		{ }
		public override void AddRoutes(IEndpointRouteBuilder app)
		{
			app.MapGet("", () =>
			{
				return Results.Ok("Server Running Successfully");
			});
		}
	}
}
