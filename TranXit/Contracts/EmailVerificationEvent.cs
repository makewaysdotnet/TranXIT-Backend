namespace Contracts
{
	public record EmailVerificationEvent
	{
		public int UserId { get; set; }
		public string Email { get; set;}
	}
}
