using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class TransportType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<JobContainer> JobContainers { get; set; } = new List<JobContainer>();
}
