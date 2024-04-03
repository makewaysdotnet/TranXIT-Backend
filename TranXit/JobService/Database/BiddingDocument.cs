using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class BiddingDocument
{
    public int Id { get; set; }

    public string? Document { get; set; }

    public string? Description { get; set; }

    public int BiddingId { get; set; }

    public string? Name { get; set; }

    public virtual Bidding Bidding { get; set; } = null!;
}
