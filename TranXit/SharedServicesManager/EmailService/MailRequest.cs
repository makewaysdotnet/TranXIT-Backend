namespace SharedServicesManager.EmailService
{
	public class MailRequest
	{
		public string EmailTo { get; set; } = string.Empty;
		public string EmailToName { get; set; } = string.Empty;
		public string EmailSubject { get; set; } = string.Empty;
		public string EmailBody { get; set; } = string.Empty;
	}
}
