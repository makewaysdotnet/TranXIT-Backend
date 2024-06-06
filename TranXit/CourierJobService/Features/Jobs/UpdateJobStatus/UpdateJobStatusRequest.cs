using CourierJobService.Enums;

namespace CourierJobService.Features.Jobs.UpdateJobStatus
{
    public record UpdateJobStatusRequest
    {
        public required JobStatusEnum Status { get; set; }
		public required int JobId { get; set; }
	}
}
