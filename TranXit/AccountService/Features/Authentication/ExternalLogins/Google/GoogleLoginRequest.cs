namespace AccountService.Features.Authentication.ExternalLogins.Google;

public class GoogleLoginRequest
{
	public string Name { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string Image { get; set; } = string.Empty;
	public string? Role { get; set; }
	public int? RoleId { get; set; }
	public string Phone { get; set; } = string.Empty;
	public DateTime? Expires { get; set; } = null;
	public ExternalLoginProviderEnum Provider { get; set; }
}

