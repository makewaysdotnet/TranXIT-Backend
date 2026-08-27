namespace CourierJobService.Features.Bids.CreateBid
{
    using CourierJobService.Features.Bids.Shared;
    using System.Text.Json.Serialization;

    public record CreateBidRequest
    {
        public required int JobId { get; set; }
        public bool? IsInsurancePolicy { get; set; }
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal PickupCharges { get; set; } = 0;
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal HandlingCharges { get; set; } = 0;
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal CustomClearanceCharges { get; set; } = 0;
        public required IEnumerable<CreateBidProposalRequest> BidProposals { get; set; }
        public IEnumerable<CreateBidChargesRequest> BidCustomCharges { get; set; } = Enumerable.Empty<CreateBidChargesRequest>();
    }
    public record CreateBidProposalRequest
    {
        public int? DeliveryTypeId { get; set; }
        public bool? IsBaseBid { get; set; }
        public DateTime? DeliveryDate { get; set; }
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal Total { get; set; } = 0;
        public IEnumerable<CreateBidProposalItemRequest> BidProposalItems { get; set; } = Enumerable.Empty<CreateBidProposalItemRequest>();
    }
    public record CreateBidProposalItemRequest
    {
        public int? JobItemId { get; set; }
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal UnitPrice { get; set; } = 0;
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal ItemTotal { get; set; } = 0;

    }

    public record CreateBidChargesRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        [JsonConverter(typeof(QuoteAmountJsonConverter))]
        public decimal Amount { get; set; } = 0;
    }
}
