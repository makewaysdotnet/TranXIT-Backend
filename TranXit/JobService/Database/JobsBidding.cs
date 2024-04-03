using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class JobsBidding
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public int BiddingId { get; set; }

    public int? OrderTrackingId { get; set; }

    public virtual Bidding Bidding { get; set; } = null!;

    public virtual Job Job { get; set; } = null!;
}
