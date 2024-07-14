using CourierJobService.Database;
using CourierJobService.Enums;

namespace CourierJobService.Helpers
{
	public static class JobsHelper
	{
		public static List<string> GetAllJobStatuses()
		{
			return new List<string>
			{
				JobStatusEnum.Open.ToString(),
				JobStatusEnum.Bidding.ToString(),
				JobStatusEnum.Won.ToString(),
				JobStatusEnum.Lost.ToString(),
				JobStatusEnum.Closed.ToString(),
				JobStatusEnum.InTransit.ToString(),
				JobStatusEnum.Delivered.ToString()
			};
		}

		public static List<int> GetSuccessfullJobStatuses()
		{
			return new List<int>
			{
				(int)JobStatusEnum.Won,
				(int)JobStatusEnum.InTransit,
				(int)JobStatusEnum.Delivered
			};
		}

		public static double GetJobRemainingTime(DateTime? expiryDateUtc, DateTime? currentTime)
			=> expiryDateUtc.HasValue &&
			   (expiryDateUtc - currentTime)!.Value.TotalSeconds > 0 ?
			   (expiryDateUtc - currentTime)!.Value.TotalSeconds : 0;

		public static (int, string) GetJobStatus(Job job, ICollection<Bidding>? biddings, int? userId)
		{
			if (!Convert.ToBoolean(job.IsJobStatusFromBid) && job.JobStatus is not null)
			{
				return (job.JobStatus!.Id!, job.JobStatus!.Status!);
			}
			else if (!Convert.ToBoolean(job.IsJobStatusFromBid) && job.JobStatus is null)
			{
				return ((int)JobStatusEnum.None, JobStatusEnum.None.ToString());
			}
			else if (Convert.ToBoolean(job.IsJobStatusFromBid) &&
					userId is not null &&
					biddings is not null &&
					biddings.Any() &&
					biddings.FirstOrDefault(b => b.UserId == userId) != null &&
					biddings.FirstOrDefault(b => b.UserId == userId)!.JobStatus != null)
			{
				return (biddings.FirstOrDefault(b => b.UserId == userId)!.JobStatus!.Id!,
					biddings.FirstOrDefault(b => b.UserId == userId)!.JobStatus!.Status!);
			}
			else if (Convert.ToBoolean(job.IsJobStatusFromBid) &&
					userId is null &&
					biddings is not null &&
					biddings.Any() &&
					biddings.FirstOrDefault(b => b.JobStatusId != (int)JobStatusEnum.Lost) != null &&
					biddings.FirstOrDefault(b => b.JobStatusId != (int)JobStatusEnum.Lost)!.JobStatus != null)
			{
				return (biddings.FirstOrDefault(b => b.JobStatusId == (int)JobStatusEnum.Lost)!.JobStatus!.Id!,
					biddings.FirstOrDefault(b => b.JobStatusId == (int)JobStatusEnum.Lost)!.JobStatus!.Status!);
			}
			else
			{
				return ((int)JobStatusEnum.None, JobStatusEnum.None.ToString());
			}
		}
	}
}
