using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class ItemUploadTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-COUR-5.ItemUploadJpegHappy200")]
	public async Task ItemUploadJpegHappy200()
	{
		// UC-COUR-5
		var response = await UploadItemImageAsync("image/jpeg", "proof.jpg", [0xFF, 0xD8, 0xFF, 0xE0]);

		await AssertHappyUploadAsync(response, "image/jpeg");
	}

	[Fact(DisplayName = "T-COUR-5.ItemUploadPngHappy200")]
	public async Task ItemUploadPngHappy200()
	{
		// UC-COUR-5
		var response = await UploadItemImageAsync("image/png", "proof.png", [0x89, 0x50, 0x4E, 0x47]);

		await AssertHappyUploadAsync(response, "image/png");
	}

	[Fact(DisplayName = "T-COUR-5.ItemUploadWebpHappy200")]
	public async Task ItemUploadWebpHappy200()
	{
		// UC-COUR-5
		var response = await UploadItemImageAsync("image/webp", "proof.webp", [0x52, 0x49, 0x46, 0x46]);

		await AssertHappyUploadAsync(response, "image/webp");
	}

	[Fact(DisplayName = "T-COUR-5.ItemUploadOversize400")]
	public async Task ItemUploadOversize400()
	{
		// UC-COUR-5
		var bytes = new byte[(5 * 1024 * 1024) + 1];
		bytes[0] = 0x89;

		var response = await UploadItemImageAsync("image/png", "too-large.png", bytes);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<ImageValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("5MB", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-COUR-5.ItemUploadNonImage400")]
	public async Task ItemUploadNonImage400()
	{
		// UC-COUR-5
		var response = await UploadItemImageAsync("text/plain", "proof.txt", [0x54, 0x58, 0x54]);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<ImageValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("JPEG", StringComparison.OrdinalIgnoreCase));
	}

	private async Task<HttpResponseMessage> UploadItemImageAsync(string contentType, string fileName, byte[] bytes)
	{
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var form = new MultipartFormDataContent
		{
			{ new StringContent("1"), "JobItemId" }
		};
		var file = new ByteArrayContent(bytes);
		file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		form.Add(file, "File", fileName);
		return await CourierClient.PostAsync("/api/jobs/job-item-image", form);
	}

	private async Task AssertHappyUploadAsync(HttpResponseMessage response, string contentType)
	{
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<ImageValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.Id.Should().Be(1);
		result.Value.Type.Should().Be(contentType);
		result.Value.Content.Should().NotBeNullOrWhiteSpace();

		await using var db = Fixture.CreateCourierJobDbContext();
		var image = await db.JobItemImages.SingleOrDefaultAsync(x => x.JobItemId == 1);
		image.Should().NotBeNull();
		image!.Type.Should().Be(contentType);
		image.Content.Should().NotBeNullOrWhiteSpace();
	}
}
