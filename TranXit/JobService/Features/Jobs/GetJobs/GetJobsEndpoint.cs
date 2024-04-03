using Carter;
using JobService.Database;
using JobService.Utils;
using MassTransit;
using MediatR;
using SharedServicesManager;

namespace JobService.Features.Jobs.GetJobs;

public class GetJobsEndpoint : CarterModule
{
	public GetJobsEndpoint()
	: base("/jobservice")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs", async (int page, int pageSize,
			IBus messageBus,
			IHttpContextAccessor httpContext,
			ISender sender) =>
		{
			var validUser = await UserValidation.IsUserValidAsync(httpContext, messageBus);
			if (validUser is null)
			{
				return Results.Unauthorized();
			}
			var query = new GetJob.Query
			{
				Page = page,
				PageSize = pageSize,
				UserId = validUser.UserId
			};
			var result = await sender.Send(query);

			return Results.Ok(result);
		}).RequireAuthorization();
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
	internal sealed class QueryHandler(JobDbContext jobDbContext)
		: IRequestHandler<Query, Result<Pagination<JobResult>>>
	{
		public async Task<Result<Pagination<JobResult>>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var jobsQuery = jobDbContext.Jobs
				.OrderByDescending(x => x.CreatedOn)
				.Select(x => new JobResult
				{
					Id = x.Id,
					CreatedOnUtc = x.CreatedOn,
					OriginCountry = x.OriginCountry!.CountryName,
					DestinationCountry = x.DestinationCountry!.CountryName,
					Status = x.JobStatus!.Status,
					StatusId = x.JobStatusId,
					MaxBid = x.Biddings.Max(y => y.TotalAmount),
					MinBid = x.Biddings.Min(y => y.TotalAmount),
					YourBid = x.Biddings.FirstOrDefault(y => y.UserId == request.UserId)!.TotalAmount
				});
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
