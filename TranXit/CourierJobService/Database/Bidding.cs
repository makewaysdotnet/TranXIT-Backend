using System;
using System.Collections.Generic;

namespace CourierJobService.Database;

public partial class Bidding
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int JobId { get; set; }

    public double TotalAmount { get; set; }

    public bool? IsInsurancePolicy { get; set; }

    public double? PickupCharges { get; set; }

    public double? HandlingCharges { get; set; }

    public double? CustomClearanceCharges { get; set; }

    public virtual ICollection<BiddingCharge> BiddingCharges { get; set; } = new List<BiddingCharge>();

    public virtual ICollection<BiddingProposal> BiddingProposals { get; set; } = new List<BiddingProposal>();

    public virtual Job Job { get; set; } = null!;
}
