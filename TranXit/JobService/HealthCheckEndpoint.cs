using Carter;

namespace JobService
{
	public class HealthCheckEndpoint : CarterModule
	{
		public HealthCheckEndpoint() : base("/jobservice")
		{ }
		public override void AddRoutes(IEndpointRouteBuilder app)
		{
			app.MapGet("", () =>
			{
				return Results.Ok("JobService Running Successfully");
			});
		}
	}
}
