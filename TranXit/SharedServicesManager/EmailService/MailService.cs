using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SharedServicesManager.EmailService;

public interface IMailService
{
	Task<bool> SendMail(MailRequest request);
}
public class MailService(IOptions<MailSettings> mailSettings) : IMailService
{
	MailSettings _mailSettings = mailSettings.Value;
	public async Task<bool> SendMail(MailRequest request)
	{
		try
		{
			using (MimeMessage emailMessage = new MimeMessage())
			{
				MailboxAddress emailFrom = new MailboxAddress(_mailSettings.SenderName, _mailSettings.SenderEmail);
				emailMessage.From.Add(emailFrom);
				MailboxAddress emailTo = new MailboxAddress(request.EmailToName, request.EmailTo);
				emailMessage.To.Add(emailTo);

				//emailMessage.Cc.Add(new MailboxAddress("Cc Receiver", "cc@example.com"));
				//emailMessage.Bcc.Add(new MailboxAddress("Bcc Receiver", "bcc@example.com"));

				emailMessage.Subject = request.EmailSubject;

				BodyBuilder emailBodyBuilder = new BodyBuilder();
				emailBodyBuilder.TextBody = request.EmailBody;

				emailMessage.Body = emailBodyBuilder.ToMessageBody();
				//this is the SmtpClient from the Mailkit.Net.Smtp namespace, not the System.Net.Mail one
				using (SmtpClient mailClient = new SmtpClient())
				{
					await mailClient.ConnectAsync(_mailSettings.Server, _mailSettings.Port, MailKit.Security.SecureSocketOptions.StartTls);
					await mailClient.AuthenticateAsync(_mailSettings.UserName, _mailSettings.Password);
					await mailClient.SendAsync(emailMessage);
					await mailClient.DisconnectAsync(true);
				}
			}

			return true;
		}
		catch (Exception)
		{
			// Exception Details
			throw;
		}
	}
}

