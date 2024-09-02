namespace AccountService.Features.AccountDocuments.UploadDocument
{
	public class UploadDocumentRequest
	{
		public required int UserId { get; set; }
		public required IFormFile File { get; set; }
	}
}
