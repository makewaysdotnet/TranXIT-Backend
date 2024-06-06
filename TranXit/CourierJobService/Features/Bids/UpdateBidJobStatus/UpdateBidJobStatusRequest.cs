using CourierJobService.Enums;

namespace CourierJobService.Features.Bids.UpdateBidJobStatus;

public record UpdateBidJobStatusRequest
{
	public required JobStatusEnum Status { get; set; }
	public required int BidId { get; set; }
	public required int BidProposalId { get; set; }
}
