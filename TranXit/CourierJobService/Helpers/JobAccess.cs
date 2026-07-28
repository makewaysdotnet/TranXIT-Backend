using System.Linq.Expressions;
using CourierJobService.Database;
using CourierJobService.Enums;

namespace CourierJobService.Helpers;

public static class JobAccess
{
	public static bool IsMarketplaceOpen(Job job, DateTime now)
		=> job.IsJobStatusFromBid is not true &&
			(job.JobStatusId == (int)JobStatusEnum.Open ||
			 job.JobStatusId == (int)JobStatusEnum.Bidding) &&
			job.ExpiryDateUtc > now;

	public static Expression<Func<Job, bool>> VisibleToCourier(int courierId, DateTime now)
		=> job =>
			job.Biddings.Any(bid => bid.UserId == courierId) ||
			(job.IsJobStatusFromBid != true &&
			 (job.JobStatusId == (int)JobStatusEnum.Open ||
			  job.JobStatusId == (int)JobStatusEnum.Bidding) &&
			 job.ExpiryDateUtc > now);
}
