using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave3;

public sealed class AuthSessionSecurityTests(SqlContainerFixture fixture)
	: IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-AUTH-9.UnverifiedLoginBlocked")]
	public async Task UnverifiedLoginBlocked()
	{
		// UC-AUTH-9
		const string email = "unverified.login@tranxit.test";
		await RegisterAsync(email);

		var response = await LoginAsync(email);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().NotBeNull();
		result.Value!.IsEmailVerified.Should().BeFalse();
		result.Value.Token.Should().BeNullOrEmpty();
		result.Value.RefreshToken.Should().BeNullOrEmpty();
		result.Error.Should().Contain(error =>
			error.Contains("verification required", StringComparison.OrdinalIgnoreCase));

		await using var db = Fixture.CreateAccountDbContext();
		(await db.RefreshTokens.CountAsync()).Should().Be(0);
	}

	[Fact(DisplayName = "T-AUTH-9.UnverifiedRefreshBlocked")]
	public async Task UnverifiedRefreshBlocked()
	{
		// UC-AUTH-9, UC-AUTH-10
		var login = await LoginSeedCustomerAsync();
		await using (var db = Fixture.CreateAccountDbContext())
		{
			var user = await db.Users.FindAsync(1);
			user!.IsEmailVerified = false;
			await db.SaveChangesAsync();
		}

		var response = await RefreshAsync(login.RefreshToken!);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		await using var verificationDb = Fixture.CreateAccountDbContext();
		(await verificationDb.RefreshTokens
			.Where(token => token.UserId == 1 && token.RevokedAtUtc == null)
			.CountAsync())
			.Should()
			.Be(0);
	}

	[Fact(DisplayName = "T-AUTH-10.LogoutRevokesFamily")]
	public async Task LogoutRevokesFamily()
	{
		// UC-AUTH-10
		var login = await LoginSeedCustomerAsync();
		var logoutRequest = RequestWithRefreshCookie(
			HttpMethod.Post,
			"/api/logout",
			login.RefreshToken!);

		var logoutResponse = await AccountClient.SendAsync(logoutRequest);
		var refreshResponse = await RefreshAsync(login.RefreshToken!);

		logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-10.PasswordResetRevokesAllSessions")]
	public async Task PasswordResetRevokesAllSessions()
	{
		// UC-AUTH-5, UC-AUTH-10
		const string resetCode = "332211";
		var firstLogin = await LoginSeedCustomerAsync();
		var secondLogin = await LoginSeedCustomerAsync();
		await using (var db = Fixture.CreateAccountDbContext())
		{
			var user = await db.Users.FindAsync(1);
			user!.VerificationCode = BCrypt.Net.BCrypt.EnhancedHashPassword(resetCode);
			user.CodeSentAtUtc = DateTime.UtcNow;
			await db.SaveChangesAsync();
		}

		var resetResponse = await AccountClient.PostAsJsonAsync("/api/reset-password", new
		{
			email = "customer.seed@tranxit.test",
			code = resetCode,
			password = "Changed1!",
			confirmPassword = "Changed1!"
		});
		var firstRefresh = await RefreshAsync(firstLogin.RefreshToken!);
		var secondRefresh = await RefreshAsync(secondLogin.RefreshToken!);

		resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		firstRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		secondRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-10.RefreshReuseRevokesDescendants")]
	public async Task RefreshReuseRevokesDescendants()
	{
		// UC-AUTH-10
		var login = await LoginSeedCustomerAsync();
		var firstRotation = await RefreshAsync(login.RefreshToken!);
		firstRotation.StatusCode.Should().Be(HttpStatusCode.OK);
		var rotated = await firstRotation.ReadApiResultAsync<LoginValue>();
		rotated.Value!.RefreshToken.Should().NotBeNullOrWhiteSpace();

		var reuseResponse = await RefreshAsync(login.RefreshToken!);
		var descendantResponse = await RefreshAsync(rotated.Value.RefreshToken!);

		reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		descendantResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-11.ConcurrentDuplicateRegistration")]
	public async Task ConcurrentDuplicateRegistration()
	{
		// UC-AUTH-11
		const string email = "concurrent.identity@tranxit.test";
		var first = RegisterAsync(email);
		var second = RegisterAsync("CONCURRENT.IDENTITY@TRANXIT.TEST");

		var responses = await Task.WhenAll(first, second);

		responses.Count(response => response.StatusCode == HttpStatusCode.OK)
			.Should()
			.Be(1);
		responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest)
			.Should()
			.Be(1);
		await using var db = Fixture.CreateAccountDbContext();
		(await db.Users.CountAsync(user =>
			user.NormalizedEmail == "concurrent.identity@tranxit.test"))
			.Should()
			.Be(1);
	}

	private Task<HttpResponseMessage> RegisterAsync(string email) =>
		AccountClient.PostAsJsonAsync("/api/register", new
		{
			email,
			password = "Password1!",
			confirmPassword = "Password1!",
			username = "Security Test",
			phone = "+92 300 0000099",
			role = "Customer"
		});

	private Task<HttpResponseMessage> LoginAsync(string email) =>
		AccountClient.PostAsJsonAsync("/api/login", new
		{
			email,
			password = "Password1!"
		});

	private async Task<LoginValue> LoginSeedCustomerAsync()
	{
		var response = await LoginAsync("customer.seed@tranxit.test");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<LoginValue>();
		return result.Value!;
	}

	private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
		AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken });

	private static HttpRequestMessage RequestWithRefreshCookie(
		HttpMethod method,
		string path,
		string refreshToken)
	{
		var request = new HttpRequestMessage(method, path);
		request.Headers.Add("Cookie", $"tranxit_refresh={refreshToken}");
		return request;
	}
}
