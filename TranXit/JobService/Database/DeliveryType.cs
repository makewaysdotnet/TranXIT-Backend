using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class DeliveryType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public int? NoOfDays { get; set; }

    public virtual ICollection<BiddingProposal> BiddingProposals { get; set; } = new List<BiddingProposal>();
}
