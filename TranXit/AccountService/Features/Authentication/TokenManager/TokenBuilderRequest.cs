namespace AccountService.Features.Authentication.TokenManager
{
	public class TokenBuilderRequest
	{
		public string UserId { get; set; } = string.Empty;
		public string Username { get; set; } = string.Empty;
		public string Role { get; set; } = string.Empty;
		public string SecretKey { get; set; } = string.Empty;
		public string Issuer { get; set; } = string.Empty;
		public string Audience { get; set; } = string.Empty;
		public string Email { get; set; } = string.Empty;
		public double ExpiryMinutes { get; set; } = 30;
		public bool EmailVerified { get; set; } = false;
	}
}
