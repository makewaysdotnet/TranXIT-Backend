using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class AuthOtpTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	private const string CustomerEmail = "customer.seed@tranxit.test";
	private const string VerificationCode = "123456";

	[Fact(DisplayName = "T-AUTH-2.VerifyOtpMissingCode400")]
	public async Task VerifyOtpMissingCode400()
	{
		// UC-AUTH-2
		await SetVerificationCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email = CustomerEmail
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact(DisplayName = "T-AUTH-2.VerifyOtpInvalidCode400")]
	public async Task VerifyOtpInvalidCode400()
	{
		// UC-AUTH-2
		await SetVerificationCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email = CustomerEmail,
			code = "654321"
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Invalid Code", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-2.VerifyOtpExpiredCode400")]
	public async Task VerifyOtpExpiredCode400()
	{
		// UC-AUTH-2
		await SetVerificationCodeAsync(DateTime.UtcNow.AddMinutes(-90));

		var response = await AccountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email = CustomerEmail,
			code = VerificationCode
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Code Expired", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-2.VerifyOtpValid200")]
	public async Task VerifyOtpValid200()
	{
		// UC-AUTH-2
		await SetVerificationCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email = CustomerEmail,
			code = VerificationCode
		});

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();

		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.IsEmailVerified.Should().BeTrue();
		user.VerificationCode.Should().BeNull();
		user.CodeSentAtUtc.Should().BeNull();
	}

	[Fact(DisplayName = "T-AUTH-2.LeadingZeroCode200")]
	public async Task VerifyOtpLeadingZeroValid200()
	{
		// UC-AUTH-2
		await SetVerificationCodeAsync(DateTime.UtcNow, "012345");

		var response = await AccountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email = CustomerEmail,
			code = "012345"
		});

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
	}

	private async Task SetVerificationCodeAsync(DateTime sentAtUtc, string code = VerificationCode)
	{
		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.VerificationCode = BCrypt.Net.BCrypt.EnhancedHashPassword(code);
		user.CodeSentAtUtc = sentAtUtc;
		user.IsEmailVerified = false;
		await db.SaveChangesAsync();
	}
}
