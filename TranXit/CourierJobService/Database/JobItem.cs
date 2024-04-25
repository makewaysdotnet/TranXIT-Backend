using System;
using System.Collections.Generic;

namespace CourierJobService.Database;

public partial class JobItem
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? ImageUrl { get; set; }

    public int? Quantity { get; set; }

    public double? Weight { get; set; }

    public double? DeclaredValue { get; set; }

    public string? Dimensions { get; set; }

    public string? Description { get; set; }

    public int? JobId { get; set; }

    public virtual ICollection<BiddingProposalItem> BiddingProposalItems { get; set; } = new List<BiddingProposalItem>();

    public virtual Job? Job { get; set; }
}
