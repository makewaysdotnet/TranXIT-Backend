namespace AccountService.Features.Authentication.AccountVerification.VerifyCode
{
	public class VerifyCodeRequest
	{
		public required string Email { get; set; }
		public required int Code { get; set; }
	}
}
