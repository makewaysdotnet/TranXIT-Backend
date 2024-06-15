namespace SharedServicesManager.EmailService
{
	public class MailRequest
	{
		public List<string> EmailTo { get; set; } = new List<string>();
		public required string EmailSubject { get; set; }
		public required string EmailBody { get; set; }
	}
}
