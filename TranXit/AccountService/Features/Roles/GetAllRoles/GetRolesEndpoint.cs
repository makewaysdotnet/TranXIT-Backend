using AccountService.Database;
using AccountService.Features.Roles.SharedResults;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.Roles.GetAllRoles;

public class GetRolesEndpoint : CarterModule
{
	public GetRolesEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/roles", async (ISender sender) =>
		{
			var result = await sender.Send(new GetAllRoles.Query());

			return Results.Ok(result);
		}).WithOpenApi()
		.WithTags("Roles")
		.Produces<Result<List<RoleResult>>>((int)HttpStatusCode.OK);
	}
}
public class GetAllRoles
{
	public sealed class Query : IRequest<Result<List<RoleResult>>>
	{
	}
	internal sealed class QueryHandler(AccountDbContext authDbContext)
		: IRequestHandler<Query, Result<List<RoleResult>>>
	{
		public async Task<Result<List<RoleResult>>> Handle(Query request,
			CancellationToken cancellationToken)
			=> await authDbContext.Roles.Select(x => new RoleResult
			{
				Id = x.Id,
				Name = x.Name
			})
			.AsNoTracking()
			.ToListAsync(cancellationToken);
	}
}