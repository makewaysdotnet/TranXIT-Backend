namespace CourierJobService.Features.Bids.CreateBid
{
    public record CreateBidRequest
    {
        public required int JobId { get; set; }
        public bool? IsInsurancePolicy { get; set; }
        public double PickupCharges { get; set; } = 0;
        public double HandlingCharges { get; set; } = 0;
        public double CustomClearanceCharges { get; set; } = 0;
        public IEnumerable<CreateBidProposalRequest> BidProposals { get; set; } = Enumerable.Empty<CreateBidProposalRequest>();
        public IEnumerable<CreateBidChargesRequest> BidCustomCharges { get; set; } = Enumerable.Empty<CreateBidChargesRequest>();
    }
    public record CreateBidProposalRequest
    {
        public int? DeliveryTypeId { get; set; }
        public bool? IsBaseBid { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public double Total { get; set; } = 0;
        public IEnumerable<CreateBidProposalItemRequest> BidProposalItems { get; set; } = Enumerable.Empty<CreateBidProposalItemRequest>();
    }
    public record CreateBidProposalItemRequest
    {
        public int? JobItemId { get; set; }
        public double UnitPrice { get; set; } = 0;
        public double ItemTotal { get; set; } = 0;

    }

    public record CreateBidChargesRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public double Amount { get; set; } = 0;
    }
}
