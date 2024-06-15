namespace CourierJobService.Features.Jobs.GetJobBidStats
{
    public class JobBidStatsResult
    {
        public int JobId { get; init; } = default;
        public double? RemainingTime { get; init; }
        public string? JobNumber { get; init; }
        public double? MinBid { get; init; }
        public double? MaxBid { get; init; }
        public double? AverageBid { get; init; }
        public int? TotalBids { get; init; }
		public string Status { get; init; } = string.Empty;
		public DateTime? CreatedOnUtc { get; init; }
    }
}
