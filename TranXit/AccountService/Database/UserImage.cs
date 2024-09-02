namespace AccountService.Database;

public partial class UserImage
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string? Name { get; set; }

    public string? Content { get; set; }

    public string? Type { get; set; }

    public virtual User User { get; set; } = null!;
}
