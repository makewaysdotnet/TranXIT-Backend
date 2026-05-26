using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class AuthResetPasswordTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	private const string CustomerEmail = "customer.seed@tranxit.test";
	private const int ResetCode = 223344;
	private const string NewPassword = "Newpass1!";

	[Fact(DisplayName = "T-AUTH-5.ResetMissingCode400")]
	public async Task ResetMissingCode400()
	{
		// UC-AUTH-5
		await SetResetCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/reset-password", new
		{
			email = CustomerEmail,
			password = NewPassword,
			confirmPassword = NewPassword
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact(DisplayName = "T-AUTH-5.ResetInvalidCode400")]
	public async Task ResetInvalidCode400()
	{
		// UC-AUTH-5
		await SetResetCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/reset-password", ResetPayload("999999"));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Invalid Code", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-5.ResetExpiredCode400")]
	public async Task ResetExpiredCode400()
	{
		// UC-AUTH-5
		await SetResetCodeAsync(DateTime.UtcNow.AddMinutes(-90));

		var response = await AccountClient.PostAsJsonAsync("/api/reset-password", ResetPayload());

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Code Expired", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-5.ResetReusedCode400")]
	public async Task ResetReusedCode400()
	{
		// UC-AUTH-5
		await SetResetCodeAsync(DateTime.UtcNow);

		var firstResponse = await AccountClient.PostAsJsonAsync("/api/reset-password", ResetPayload());
		firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

		var reusedResponse = await AccountClient.PostAsJsonAsync("/api/reset-password", ResetPayload());

		reusedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await reusedResponse.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Invalid Code", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-5.ResetValid200")]
	public async Task ResetValid200()
	{
		// UC-AUTH-5
		await SetResetCodeAsync(DateTime.UtcNow);

		var response = await AccountClient.PostAsJsonAsync("/api/reset-password", ResetPayload());

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<bool>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();

		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.VerificationCode.Should().BeNull();
		user.CodeSentAtUtc.Should().BeNull();

		var loginResponse = await AccountClient.PostAsJsonAsync("/api/login", new
		{
			email = CustomerEmail,
			password = NewPassword
		});
		loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	private static object ResetPayload(string code = "223344")
		=> new
		{
			email = CustomerEmail,
			code,
			password = NewPassword,
			confirmPassword = NewPassword
		};

	private async Task SetResetCodeAsync(DateTime sentAtUtc)
	{
		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.VerificationCode = ResetCode;
		user.CodeSentAtUtc = sentAtUtc;
		await db.SaveChangesAsync();
	}
}
