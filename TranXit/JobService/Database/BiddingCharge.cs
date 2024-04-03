using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class BiddingCharge
{
    public int Id { get; set; }

    public int? BiddingId { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public double? Amount { get; set; }

    public virtual Bidding? Bidding { get; set; }
}
