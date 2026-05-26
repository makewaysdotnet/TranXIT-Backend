using System.IdentityModel.Tokens.Jwt;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class AuthLoginTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-AUTH-3.LoginHappy")]
	public async Task LoginHappy()
	{
		// UC-AUTH-3
		var response = await AccountClient.PostAsJsonAsync("/api/login", new
		{
			email = "customer.seed@tranxit.test",
			password = "Password1!"
		});

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.Role.Should().Be("Customer");
		result.Value.RoleId.Should().Be(1);
		result.Value.IsEmailVerified.Should().BeTrue();
		result.Value.Token.Should().NotBeNullOrWhiteSpace();
		result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
		result.Value.RefreshTokenExpires.Should().NotBeNullOrWhiteSpace();

		var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.Token);
		token.Issuer.Should().Be(TestConfiguration.Issuer);
		token.Audiences.Should().Contain(TestConfiguration.Audience);
		token.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Customer");
		token.Claims.Should().Contain(c => c.Type == "UserId" && c.Value == "1");
		token.Claims.Should().Contain(c => c.Type == "EmailVerified" && c.Value == "True");
	}

	[Fact(DisplayName = "T-AUTH-3.RefreshHappy")]
	public async Task RefreshHappy()
	{
		// UC-AUTH-3
		var login = await LoginSeedCustomerAsync();
		var response = await RefreshAsync(login.RefreshToken!);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.Token.Should().NotBeNullOrWhiteSpace();
		result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
		result.Value.RefreshToken.Should().NotBe(login.RefreshToken);
		result.Value.Role.Should().Be("Customer");
	}

	[Fact(DisplayName = "T-AUTH-3.RefreshRevoked401")]
	public async Task RefreshRevoked401()
	{
		// UC-AUTH-3
		var login = await LoginSeedCustomerAsync();
		var firstRefresh = await RefreshAsync(login.RefreshToken!);
		firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

		var reusedRefresh = await RefreshAsync(login.RefreshToken!);

		reusedRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		var result = await reusedRefresh.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("invalid", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-3.ExpiryTightened")]
	public async Task ExpiryTightened()
	{
		// UC-AUTH-3
		var login = await LoginSeedCustomerAsync();

		var token = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
		var lifetime = token.ValidTo - DateTime.UtcNow;

		lifetime.Should().BeLessThan(TimeSpan.FromMinutes(65));
		lifetime.Should().BeGreaterThan(TimeSpan.FromMinutes(55));
	}

	[Fact(DisplayName = "T-AUTH-3.LoginWrongCredentials400")]
	public async Task LoginWrongCredentials400()
	{
		// UC-AUTH-3
		var response = await AccountClient.PostAsJsonAsync("/api/login", new
		{
			email = "customer.seed@tranxit.test",
			password = "Wrongpass1!"
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Invalid password", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-AUTH-3.WrongIssuerToken401")]
	public async Task WrongIssuerToken401()
	{
		// UC-AUTH-3
		AccountClient.AuthenticateAs(Tokens.ForUser(1, "Customer", issuer: "TranXIT.WrongIssuer"));

		var response = await AccountClient.GetAsync("/api/users/1");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-3.ExpiredToken401")]
	public async Task ExpiredToken401()
	{
		// UC-AUTH-3
		AccountClient.AuthenticateAs(Tokens.ForUser(1, "Customer", expiryMinutes: -10));

		var response = await AccountClient.GetAsync("/api/users/1");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	private async Task<LoginValue> LoginSeedCustomerAsync()
	{
		var response = await AccountClient.PostAsJsonAsync("/api/login", new
		{
			email = "customer.seed@tranxit.test",
			password = "Password1!"
		});

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.Value.Should().NotBeNull();
		return result.Value!;
	}

	private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "/api/refresh");
		request.Headers.Add("Cookie", $"tranxit_refresh={refreshToken}");
		return await AccountClient.SendAsync(request);
	}
}
