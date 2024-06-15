namespace CourierJobService.Features.Jobs.CreateJob
{
    public record CreateJobRequest
    {
        public required int CourierModeId { get; set; }
        public required int CargoModeId { get; set; }
        public int? ItemTypeId { get; set; }
        public int? OriginCountryId { get; set; }
        public int? DestinationCountryId { get; set; }
        public int? OriginCityId { get; set; }
        public int? DestinationCityId { get; set; }
        public string? OriginAddress { get; init; }
        public string? DestinationAddress { get; init; }
        public required string RecipientContact { get; init; }
        public required string RecipientName { get; init; }
        public required string RecipientEmail { get; init; }
        public DateTime? PickupDateUtc { get; init; }
        public DateTime? ExpiryDateUtc { get; init; }
        public IEnumerable<CreateJobItemRequest> JobItems { get; set; } = Enumerable.Empty<CreateJobItemRequest>();

    }

    public record CreateJobItemRequest
    {
        public string? ItemName { get; init; }
        public string? ImageUrl { get; init; }
        public string? Dimensions { get; init; }
        public string? Description { get; init; }
        public int? Quantity { get; set; }
        public int? ItemTypeId { get; set; }
        public double? Weight { get; set; }
        public double? DeclaredValue { get; set; }
    }
}
