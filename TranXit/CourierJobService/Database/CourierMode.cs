using System;
using System.Collections.Generic;

namespace CourierJobService.Database;

public partial class CourierMode
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
