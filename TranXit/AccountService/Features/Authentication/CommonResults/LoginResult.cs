using AccountService.Features.Authentication.ExternalLogins;

namespace AccountService.Features.Authentication.CommonResults
{
	public class LoginResult
	{
		public int Id { get; init; } = default;
		public string Name { get; init; } = string.Empty;
		public string Email { get; init; } = string.Empty;
		public string? Role { get; init; } = null;
		public ExternalLoginProviderEnum? Provider { get; init; } = null;
		public string Token { get; init; } = string.Empty;
	}
}
