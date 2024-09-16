using SharedServicesManager;

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
        public string? OriginCity { get; init; }
        public string? DestinationCity { get; init; }
        public string? OriginCountry { get; init; }
        public string? DestinationCountry { get; init; }
        public string? JobNumber { get; init; }
        public DateTime? PickupDateUtc { get; init; }
        public string? Status { get; init; }
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
        public ImageResult? ImageResult { get; init; } 
    }
}
