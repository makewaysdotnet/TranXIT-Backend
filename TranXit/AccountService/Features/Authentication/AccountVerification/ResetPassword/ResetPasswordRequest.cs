namespace AccountService.Features.Authentication.AccountVerification.ResetPassword
{
	public class ResetPasswordRequest
	{
		public required string Email { get; set; }
		public required string Password { get; set; }
		public required string ConfirmPassword { get; set; }

	}
}
