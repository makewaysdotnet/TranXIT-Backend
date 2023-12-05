namespace AuthService.Database;

public class User
{
	public int Id { get; set; }

	public string Email { get; set; } = null!;

	public string PasswordHash { get; set; } = null!;

	public string? SecurityStamp { get; set; }

	public string Username { get; set; } = null!;

	public int? RoleId { get; set; }

	public Role? Role { get; set; }
}
