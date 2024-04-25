namespace CourierJobService.Features.Biddings.CreateBid
{
	public record CreateBidRequest
	{
		public required int JobId { get; set; }
		public double TotalAmount { get; set; }
		public bool? IsInsurancePolicy { get; set; }
		public double? PickupCharges { get; set; }
		public double? HandlingCharges { get; set; }
		public double? CustomClearanceCharges { get; set; }
		public IEnumerable<CreateBidProposalRequest> BidProposals { get; set; } = Enumerable.Empty<CreateBidProposalRequest>();
	}
	public record CreateBidProposalRequest
	{
		public int? DeliveryTypeId { get; set; }
		public bool? IsBaseBid { get; set; }
		public DateTime? DeliveryDate { get; set; }
		public double? Total { get; set; }
		public IEnumerable<CreateBidProposalItemRequest> BidProposalItems { get; set; } = Enumerable.Empty<CreateBidProposalItemRequest>();
	}
	public record CreateBidProposalItemRequest
	{
		public int? JobItemId { get; set; }
		public double? UnitPrice { get; set; }
		public double? ItemTotal { get; set; }

	}
}
