namespace AccountService.Database;

public partial class RefreshToken
{
	public int Id { get; set; }

	public int UserId { get; set; }

	public string TokenHash { get; set; } = string.Empty;

	public DateTime ExpiresAtUtc { get; set; }

	public DateTime? RevokedAtUtc { get; set; }

	public DateTime CreatedAtUtc { get; set; }

	public virtual User User { get; set; } = null!;
}
