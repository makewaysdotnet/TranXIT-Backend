namespace AccountService.Features.Authentication.AccountVerification.ResetPassword
{
	public class ResetPasswordRequest
	{
		public string Email { get; set; } = string.Empty;
		public string Code { get; set; } = string.Empty;
		public string Password { get; set; } = string.Empty;
		public string ConfirmPassword { get; set; } = string.Empty;

	}
}
