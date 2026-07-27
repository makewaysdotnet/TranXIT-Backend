using Carter;
using CourierJobService.Database;
using CourierJobService.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobs;

public class GetJobsEndpoint : CarterModule
{
	public GetJobsEndpoint()
	: base("/api")
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
		}).RequireAuthorization("CourierPolicy")
		.WithTags("Jobs")
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
			var currentTime = DateTime.UtcNow;
			var jobsQuery = jobDbContext.Jobs
				.Where(JobAccess.VisibleToCourier(request.UserId, currentTime))
				.OrderByDescending(x => x.CreatedOnUtc)
				.Include(x => x.OriginCountry)
				.Include(x => x.OriginCity)
				.Include(x => x.DestinationCountry)
				.Include(x => x.DestinationCity)
				.Include(x => x.JobStatus)
				.Include(x => x.Biddings).ThenInclude(y => y.JobStatus)
				.AsSplitQuery()
				.AsNoTracking()
				.Select(x => MapToJobResult(x, request.UserId, currentTime));

			var paginatedResponse = await Pagination<JobResult>
				.CreateAsync(jobsQuery, request.Page, request.PageSize);
			return paginatedResponse;
		}

		private static JobResult MapToJobResult(Job job, int userId, DateTime currentTime)
		{
			var status = JobsHelper.GetJobStatus(job, job.Biddings, userId);
			return new JobResult
			{
				Id = job.Id,
				CustomerId = job.UserId,
				CreatedOnUtc = job.CreatedOnUtc,
				OriginCountry = job.OriginCountry?.CountryName,
				DestinationCountry = job.DestinationCountry?.CountryName,
				OriginCity = job.OriginCity?.CityName,
				DestinationCity = job.DestinationCity?.CityName,
				Status = status.Item2,
				StatusId = status.Item1,
				JobNumber = job.JobNumber,
				MaxBid = job.Biddings.Any() ? job.Biddings.Max(y => y.TotalAmount) : 0,
				MinBid = job.Biddings.Any() ? job.Biddings.Min(y => y.TotalAmount) : 0,
				YourBid = job.Biddings.Any() ? 
					job.Biddings.FirstOrDefault(y => y.UserId == userId)?.TotalAmount : 0,
				RemainingTime = JobsHelper.GetJobRemainingTime(job.ExpiryDateUtc, currentTime)
			};
		}
	}
}
