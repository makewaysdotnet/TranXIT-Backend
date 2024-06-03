namespace CourierJobService.Database;

public partial class Job
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? OriginCountryId { get; set; }

    public int? OriginCityId { get; set; }

    public string? OriginAddress { get; set; }

    public int? DestinationCountryId { get; set; }

    public int? DestinationCityId { get; set; }

    public string? DestinationAddress { get; set; }

    public string? Comments { get; set; }

    public int? JobStatusId { get; set; }

    public DateTime? CreatedOnUtc { get; set; }

    public DateTime? PickupDateUtc { get; set; }

    public int? CargoModeId { get; set; }

    public int? CourierModeId { get; set; }

    public string? JobNumber { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientContact { get; set; }

    public string? RecipientEmail { get; set; }

    public DateTime? ExpiryDateUtc { get; set; }

    public virtual ICollection<Bidding> Biddings { get; set; } = new List<Bidding>();

    public virtual CargoMode? CargoMode { get; set; }

    public virtual CourierMode? CourierMode { get; set; }

    public virtual City? DestinationCity { get; set; }

    public virtual Country? DestinationCountry { get; set; }

    public virtual ICollection<JobItem> JobItems { get; set; } = new List<JobItem>();

    public virtual JobStatus? JobStatus { get; set; }

    public virtual City? OriginCity { get; set; }

    public virtual Country? OriginCountry { get; set; }
}
