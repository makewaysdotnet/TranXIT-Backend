using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class IncoTerm
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Bidding> Biddings { get; set; } = new List<Bidding>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
