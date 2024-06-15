namespace CourierJobService.Features.Bids.GetJobUserBids
{
	public class JobUserBidResult
	{
		public int BidId { get; set; }
		public double BidMinOffer { get; set; } = 0;
		public string CourierName { get; set; } = string.Empty;
		public int CourierId { get; set; }
		public string CourierAddress { get; set; } = string.Empty;
	}
}
