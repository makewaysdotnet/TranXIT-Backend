using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class AccessControlRegressionTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-3.GetUserCrossUser403")]
	public async Task GetUserCrossUser403()
	{
		// UC-NFR-3
		AccountClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await AccountClient.GetAsync("/api/users/2");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact(DisplayName = "T-NFR-3.UploadImageCrossUser403")]
	public async Task UploadImageCrossUser403()
	{
		// UC-NFR-3
		AccountClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var form = ImageForm("UserId", "2");

		var response = await AccountClient.PostAsync("/api/upload/image", form);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact(DisplayName = "T-NFR-3.UploadJobItemImageCrossUser403")]
	public async Task UploadJobItemImageCrossUser403()
	{
		// UC-NFR-3
		await SeedOtherCustomerJobItemAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var form = ImageForm("JobItemId", "2");

		var response = await CourierClient.PostAsync("/api/jobs/job-item-image", form);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	private static MultipartFormDataContent ImageForm(string idName, string idValue)
	{
		var form = new MultipartFormDataContent
		{
			{ new StringContent(idValue), idName }
		};
		var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
		file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		form.Add(file, "File", "proof.png");
		return form;
	}

	private async Task SeedOtherCustomerJobItemAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Jobs] ON;
			INSERT INTO [Jobs]
				([Id], [UserId], [OriginCountryId], [OriginCityId], [OriginAddress],
				 [DestinationCountryId], [DestinationCityId], [DestinationAddress], [Comments],
				 [JobStatusId], [CreatedOnUtc], [PickupDateUtc], [CargoModeId], [CourierModeId],
				 [JobNumber], [RecipientName], [RecipientContact], [RecipientEmail], [ExpiryDateUtc],
				 [IsJobStatusFromBid])
			VALUES
				(2, 4, 1, 1, 'Other customer origin',
				 2, 3, 'Other customer destination', 'Other customer job',
				 5, SYSUTCDATETIME(), DATEADD(day, 3, SYSUTCDATETIME()), 1, 1,
				 'TX4001', 'Other Recipient', '+49 40 111111', 'other-recipient@example.com',
				 DATEADD(day, 6, SYSUTCDATETIME()), 0);
			SET IDENTITY_INSERT [Jobs] OFF;

			SET IDENTITY_INSERT [JobItems] ON;
			INSERT INTO [JobItems]
				([Id], [Name], [ImageUrl], [Quantity], [Weight], [DeclaredValue], [Dimensions], [Description], [JobId], [ItemTypeId])
			VALUES
				(2, 'Other cartons', NULL, 1, 10, 1000, '1m', 'Cross-user protected item', 2, 1);
			SET IDENTITY_INSERT [JobItems] OFF;
			""");
	}
}
