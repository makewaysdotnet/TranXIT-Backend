using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class Port
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Bidding> BiddingDestinationPorts { get; set; } = new List<Bidding>();

    public virtual ICollection<Bidding> BiddingOriginPorts { get; set; } = new List<Bidding>();

    public virtual ICollection<Job> JobDestinationPorts { get; set; } = new List<Job>();

    public virtual ICollection<Job> JobOriginPorts { get; set; } = new List<Job>();
}
