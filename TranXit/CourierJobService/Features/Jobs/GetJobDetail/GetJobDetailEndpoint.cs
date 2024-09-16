using Carter;
using CourierJobService.Database;
using CourierJobService.Helpers;
using CourierJobService.Requests;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.GetJobDetail;

public class GetJobDetailEndpoint : CarterModule
{
	public GetJobDetailEndpoint()
	: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapGet("/jobs/{jobId:int}/details", async (int jobId,
			ISender sender) =>
		{
			var query = new GetJobDetail.Query { jobId = jobId };
			var result = await sender.Send(query);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization("CustomerCourierPolicy")
		.WithTags("Jobs")
		.WithOpenApi()
		.Produces<Result<JobDetailResult>>((int)HttpStatusCode.OK)
		.Produces<Result<JobDetailResult>>((int)HttpStatusCode.BadRequest);
	}
}
public class GetJobDetail
{
	public sealed class Query : IRequest<Result<JobDetailResult>>
	{
		public required int jobId { get; set; }
	}
	internal sealed class QueryHandler(CourierJobDbContext jobDbContext,
		IBus messageBus,
		IHttpContextAccessor httpContext)
		: IRequestHandler<Query, Result<JobDetailResult>>
	{
		public async Task<Result<JobDetailResult>> Handle(Query request,
			CancellationToken cancellationToken)
		{
			var jobDetail = await jobDbContext.Jobs
				.Include(x => x.OriginCountry)
				.Include(x => x.OriginCity)
				.Include(x => x.DestinationCountry)
				.Include(x => x.DestinationCity)
				.Include(x => x.CargoMode)
				.Include(x => x.CourierMode)
				.Include(x => x.JobStatus)
				.Include(x => x.JobItems)
				.Include(x => x.Biddings).ThenInclude(y => y.JobStatus)
				.AsSplitQuery()
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == request.jobId, cancellationToken);

			if (jobDetail is null)
			{
				return new Error("Job Detail not found");
			}
			var currentUserRole = HttpContextUser.GetCurrentUserRole(httpContext);

			var jobDetaliResult = new JobDetailResult
			{
				JobId = jobDetail.Id,
				UserId = jobDetail.UserId,
				DestinationAddress = jobDetail.DestinationAddress!,
				CargoMode = jobDetail.CargoMode!.Name!,
				CourierMode = jobDetail.CourierMode!.Name!,
				OriginCountry = jobDetail.OriginCountry!.CountryName,
				DestinationCountry = jobDetail.DestinationCountry!.CountryName,
				OriginCity = jobDetail.OriginCity!.CityName,
				DestinationCity = jobDetail.DestinationCity!.CityName,
				PickupDateUtc = jobDetail.PickupDateUtc,
				JobNumber = jobDetail.JobNumber,
				Status = currentUserRole == "Customer" ?
					JobsHelper.GetJobStatus(jobDetail, jobDetail.Biddings, null).Item2 :
					JobsHelper.GetJobStatus(jobDetail, jobDetail.Biddings, HttpContextUser.GetCurrentUserId(httpContext)).Item2,
				JobItems = jobDetail.JobItems!.Select(y => new JobItemResult
				{
					JobItemId = y.Id,
					ItemName = y.Name ?? string.Empty,
					DeclaredValue = y.DeclaredValue ?? 0.0,
					Description = y.Description ?? string.Empty,
					Quantity = y.Quantity ?? 0,
					Size = y.Dimensions ?? string.Empty,
					Weight = y.Weight ?? 0.0,
					ImageResult = new ImageResult
					{
						Id = y.JobItemImage?.JobItemId,
						Name = y.JobItemImage?.Name,
						Content = y.JobItemImage?.Content,
						Type = y.JobItemImage?.Type
					}
				})
			};

			var userResult = await UserRequest.GetUserAsync(jobDetaliResult!.UserId, messageBus);
			if (userResult is null)
			{
				return new Error("User not found");
			}
			jobDetaliResult.CustomerName = userResult.UserName!;
			return jobDetaliResult;
		}
	}
}
