namespace CourierJobService.Features.Jobs.GetJobDetail
{
    public class JobDetailResult
    {
        public int JobId { get; init; } = default;
        public int UserId { get; init; } = default;
        public string DestinationAddress { get; init; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CourierMode { get; init; } = string.Empty;
        public string CargoMode { get; init; } = string.Empty;
        public IEnumerable<JobItemResult> JobItems { get; init; } = [];
    }
    public class JobItemResult
    {
        public int JobItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public double? Weight { get; init; }
        public double? DeclaredValue { get; init; }
        public int? Quantity { get; init; }
        public string Size { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
