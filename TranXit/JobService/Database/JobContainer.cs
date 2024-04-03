using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class JobContainer
{
    public int Id { get; set; }

    public int? ContainerSizeId { get; set; }

    public double? Weight { get; set; }

    public int? TransportTypeId { get; set; }

    public int JobId { get; set; }

    public virtual ContainerSize? ContainerSize { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual TransportType? TransportType { get; set; }
}
