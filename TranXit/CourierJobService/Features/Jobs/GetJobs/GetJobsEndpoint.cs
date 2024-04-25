using Carter;
using CourierJobService.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobs;

public class GetJobsEndpoint : CarterModule
{
	public GetJobsEndpoint()
	: base("/courierjobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs", async (int page, int pageSize,
			IHttpContextAccessor httpContext,
			ISender sender) =>
		{
			var query = new GetJob.Query
			{
				Page = page,
				PageSize = pageSize,
				UserId = HttpContextUser.GetCurrentUserId(httpContext)
			};
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization()
		.WithOpenApi()
		.Produces<Result<Pagination<JobResult>>>((int)HttpStatusCode.OK)
		.Produces<Result<Pagination<JobResult>>>((int)HttpStatusCode.BadRequest);
	}
}
public class GetJob
{
	public sealed class Query : IRequest<Result<Pagination<JobResult>>>
	{
		public int Page { get; set; }
		public int PageSize { get; set; }
		public int UserId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext)
		: IRequestHandler<Query, Result<Pagination<JobResult>>>
	{
		public async Task<Result<Pagination<JobResult>>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var jobsQuery = jobDbContext.Jobs
				.OrderByDescending(x => x.CreatedOnUtc)
				.Select(x => new JobResult
				{
					Id = x.Id,
					CreatedOnUtc = x.CreatedOnUtc,
					OriginCountry = x.OriginCountry!.CountryName,
					DestinationCountry = x.DestinationCountry!.CountryName,
					Status = x.JobStatus!.Status,
					StatusId = x.JobStatusId,
					MaxBid = x.Biddings.Max(y => y.TotalAmount),
					MinBid = x.Biddings.Min(y => y.TotalAmount),
					YourBid = x.Biddings.FirstOrDefault(y => y.UserId == request.UserId)!.TotalAmount
				})
				.AsSplitQuery()
				.AsNoTracking();
			if (jobsQuery is null)
			{
				return new Error("Jobs not found");
			}
			var paginatedResponse = await Pagination<JobResult>
				.CreateAsync(jobsQuery, request.Page, request.PageSize);
			return paginatedResponse;
		}
	}
}
