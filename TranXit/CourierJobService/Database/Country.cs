using System;
using System.Collections.Generic;

namespace CourierJobService.Database;

public partial class Country
{
    public int Id { get; set; }

    public string? CountryName { get; set; }

    public virtual ICollection<City> Cities { get; set; } = new List<City>();

    public virtual ICollection<Job> JobDestinationCountries { get; set; } = new List<Job>();

    public virtual ICollection<Job> JobOriginCountries { get; set; } = new List<Job>();
}
