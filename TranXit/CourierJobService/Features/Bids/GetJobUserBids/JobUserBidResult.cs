namespace CourierJobService.Features.Bids.GetJobUserBids
{
	public class JobUserBidResult
	{
		public int BidId { get; set; }
		public int? BidProposalId { get; set; }
		public List<int> BidProposalIds { get; set; } = [];
		public int? AcceptedBidProposalId { get; set; }
		public int? BidStatusId { get; set; }
		public bool IsJobAwarded { get; set; }
		public bool CanAccept { get; set; }
		public List<JobUserBidProposalResult> BidProposals { get; set; } = [];
		public double BidMinOffer { get; set; } = 0;
		public string CourierName { get; set; } = string.Empty;
		public int CourierId { get; set; }
		public string CourierAddress { get; set; } = string.Empty;
	}

	public class JobUserBidProposalResult
	{
		public int BidProposalId { get; set; }
		public bool IsBaseBid { get; set; }
		public double? Total { get; set; }
		public DateTime? DeliveryDateUtc { get; set; }
		public string? DeliveryType { get; set; }
	}
}
