using System;
using System.Collections.Generic;

namespace JobService.Database;

public partial class Job
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? OriginCountryId { get; set; }

    public int? OriginCityId { get; set; }

    public int? OriginPortId { get; set; }

    public string? OriginAddress { get; set; }

    public int? DestinationCountryId { get; set; }

    public int? DestinationCityId { get; set; }

    public int? DestinationPortId { get; set; }

    public string? DestinationAddress { get; set; }

    public decimal? ShipmentWeight { get; set; }

    public double? ContainerSize { get; set; }

    public int? NoOfBoxes { get; set; }

    public double? BoxSize { get; set; }

    public string? Commodity { get; set; }

    public string? Hscode { get; set; }

    public string? Comments { get; set; }

    public string? PackingListPdf { get; set; }

    public DateTime? CargoReadiness { get; set; }

    public int? JobStatusId { get; set; }

    public DateTime? CreatedOn { get; set; }

    public DateTime? EstimatedTime { get; set; }

    public int? IncoTermId { get; set; }

    public virtual ICollection<Bidding> Biddings { get; set; } = new List<Bidding>();

    public virtual City? DestinationCity { get; set; }

    public virtual Country? DestinationCountry { get; set; }

    public virtual Port? DestinationPort { get; set; }

    public virtual IncoTerm? IncoTerm { get; set; }

    public virtual ICollection<JobContainer> JobContainers { get; set; } = new List<JobContainer>();

    public virtual ICollection<JobItem> JobItems { get; set; } = new List<JobItem>();

    public virtual JobStatus? JobStatus { get; set; }

    public virtual City? OriginCity { get; set; }

    public virtual Country? OriginCountry { get; set; }

    public virtual Port? OriginPort { get; set; }
}
