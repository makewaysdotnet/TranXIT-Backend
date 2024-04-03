using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class BiddingProposal
{
    public int Id { get; set; }

    public int? BiddingId { get; set; }

    public int? DeliveryTypeId { get; set; }

    public bool? IsBaseBid { get; set; }

    public DateTime? DeliveryDateUtc { get; set; }

    public double? Total { get; set; }

    public virtual Bidding? Bidding { get; set; }

    public virtual DeliveryType? DeliveryType { get; set; }
}
