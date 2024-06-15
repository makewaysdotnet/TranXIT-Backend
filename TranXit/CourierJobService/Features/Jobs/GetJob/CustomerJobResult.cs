namespace CourierJobService.Features.Jobs.GetJob
{
    public class CustomerJobResult
    {
        public int Id { get; init; } = default;
        public int CustomerId { get; init; } = default;
        public string? OriginCountry { get; init; }
        public string? DestinationCountry { get; init; }
        public string? OriginCity { get; init; }
        public string? DestinationCity { get; init; }
        public string? OriginAddress { get; init; }
        public string? DestinationAddress { get; init; }
        public string? JobNumber { get; init; }
        public DateTime? CreatedOnUtc { get; init; }
        public string? Status { get; init; }
        public int? StatusId { get; init; }
    }
}
