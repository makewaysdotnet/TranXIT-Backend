namespace CourierJobService.Database;

public partial class BiddingProposalItem
{
    public int Id { get; set; }

    public int? BiddingProposalId { get; set; }

    public int? JobItemId { get; set; }

    public double? UnitPrice { get; set; }

    public double? ItemTotal { get; set; }

    public virtual BiddingProposal? BiddingProposal { get; set; }

    public virtual JobItem? JobItem { get; set; }
}
