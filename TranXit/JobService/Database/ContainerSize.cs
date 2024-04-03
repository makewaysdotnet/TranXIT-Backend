using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class ContainerSize
{
    public int Id { get; set; }

    public string? Size { get; set; }

    public virtual ICollection<JobContainer> JobContainers { get; set; } = new List<JobContainer>();
}
