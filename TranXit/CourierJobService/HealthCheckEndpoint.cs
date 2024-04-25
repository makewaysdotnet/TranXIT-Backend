using Carter;

namespace CourierJobService
{
	public class HealthCheckEndpoint : CarterModule
	{
		public HealthCheckEndpoint() : base("/courierjobservice")
		{ }
		public override void AddRoutes(IEndpointRouteBuilder app)
		{
			app.MapGet("", () =>
			{
				return Results.Ok("Courier Job Service Running Successfully");
			});
		}
	}
}
