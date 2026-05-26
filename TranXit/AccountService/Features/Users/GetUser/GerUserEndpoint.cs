using AccountService.Database;
using Carter;
using Mapster;
using MediatR;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace AccountService.Features.Users.GetUser;

public class GetUserEndpoint : CarterModule
{
	public GetUserEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/users/{id:int}", async (int id, ISender sender, IHttpContextAccessor httpContext) =>
		{
			var currentUserId = HttpContextUser.GetCurrentUserId(httpContext);
			var currentUserRole = HttpContextUser.GetCurrentUserRole(httpContext);
			if (id != currentUserId && !string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}

			var query = new GetUser.Query { Id = id };
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithTags("Users")
		.WithOpenApi()
		.Produces<Result<UserResult>>((int)HttpStatusCode.OK)
		.Produces((int)HttpStatusCode.Forbidden);
	}
}
public class GetUser
{
	public sealed class Query : IRequest<Result<UserResult>>
	{
		public int Id { get; set; }
	}
	internal sealed class QueryHandler(AccountDbContext authDbContext)
		: IRequestHandler<Query, Result<UserResult>>
	{
		public async Task<Result<UserResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var userResponse = await authDbContext.Users
				.FindAsync(request.Id, cancellationToken);
			if (userResponse is null)
			{
				return new Error("User not found");
			}
			return userResponse.Adapt<UserResult>();
		}
	}
}
