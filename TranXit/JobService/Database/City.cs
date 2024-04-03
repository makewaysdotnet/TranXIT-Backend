using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class City
{
    public int Id { get; set; }

    public int CountryId { get; set; }

    public string CityName { get; set; } = null!;

    public virtual Country Country { get; set; } = null!;

    public virtual ICollection<Job> JobDestinationCities { get; set; } = new List<Job>();

    public virtual ICollection<Job> JobOriginCities { get; set; } = new List<Job>();
}
