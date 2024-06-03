namespace CourierJobService.Features.Jobs.GetJobStats
{
    public class JobStatsResult
    {
		public int Won { get; init; } = 0;
		public int TotalShipments { get; init; } = 0;
		public int Delivered { get; init; } = 0;
		public int InTransit { get; init; } = 0;
	}
}
