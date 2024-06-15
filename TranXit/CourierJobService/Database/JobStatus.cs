namespace CourierJobService.Database;

public partial class JobStatus
{
    public int Id { get; set; }

    public string? Status { get; set; }

    public virtual ICollection<Bidding> Biddings { get; set; } = new List<Bidding>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
