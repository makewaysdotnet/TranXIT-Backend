namespace CourierJobService.Database;

public partial class ItemType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<JobItem> JobItems { get; set; } = new List<JobItem>();
}
