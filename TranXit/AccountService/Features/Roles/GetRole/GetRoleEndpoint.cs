using AccountService.Database;
using AccountService.Features.Roles.SharedResults;
using Carter;
using Mapster;
using MediatR;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Roles.GetRole;

public class GetRoleEndpoint : CarterModule
{
	public GetRoleEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/roles/{id:int}", async (int id, ISender sender) =>
		{
			var query = new GetRole.Query { Id = id };
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<RoleResult>>((int)HttpStatusCode.OK);
	}
}
public class GetRole
{
	public sealed class Query : IRequest<Result<RoleResult>>
	{
		public int Id { get; set; }
	}
	internal sealed class QueryHandler(AccountDbContext authDbContext)
		: IRequestHandler<Query, Result<RoleResult>>
	{
		public async Task<Result<RoleResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var roleResponse = await authDbContext.Roles
				.FindAsync(request.Id, cancellationToken);
			if (roleResponse is null)
			{
				return new Error("Role not found");
			}
			return roleResponse.Adapt<RoleResult>();
		}
	}
}
