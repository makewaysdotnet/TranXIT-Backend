namespace AccountService.Database;

public partial class UserFile
{
	public int Id { get; set; }

	public string? Name { get; set; }

	public string? Content { get; set; }

	public string? Type { get; set; }

	public int UserId { get; set; }

	public virtual User User { get; set; } = null!;
}
