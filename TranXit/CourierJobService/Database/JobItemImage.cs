namespace CourierJobService.Database;

public partial class JobItemImage
{
    public int JobItemId { get; set; }

    public string? Name { get; set; }

    public string? Content { get; set; }

    public string? Type { get; set; }

    public virtual JobItem JobItem { get; set; } = null!;
}
