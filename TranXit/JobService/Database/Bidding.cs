using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class Bidding
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int JobId { get; set; }

    public int? OriginPortId { get; set; }

    public int? DestinationPortId { get; set; }

    public DateTime? ScheduleStartDate { get; set; }

    public DateTime? ScheduleEndDate { get; set; }

    public int? IncoTermId { get; set; }

    public double? ExWorksPrice { get; set; }

    public double? FreightAmount { get; set; }

    public double? EndorsementCharges { get; set; }

    public string? TransitTime { get; set; }

    public int? FreeDays { get; set; }

    public double TotalAmount { get; set; }

    public string? CargoType { get; set; }

    public bool? IsInsurancePolicy { get; set; }

    public double? PickupCharges { get; set; }

    public double? HandlingCharges { get; set; }

    public double? CustomClearanceCharges { get; set; }

    public virtual ICollection<BiddingCharge> BiddingCharges { get; set; } = new List<BiddingCharge>();

    public virtual ICollection<BiddingDocument> BiddingDocuments { get; set; } = new List<BiddingDocument>();

    public virtual ICollection<BiddingProposal> BiddingProposals { get; set; } = new List<BiddingProposal>();

    public virtual Port? DestinationPort { get; set; }

    public virtual IncoTerm? IncoTerm { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual Port? OriginPort { get; set; }
}
