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

		var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.Token);
		token.Issuer.Should().Be(TestConfiguration.Issuer);
		token.Audiences.Should().Contain(TestConfiguration.Audience);
		token.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Customer");
		token.Claims.Should().Contain(c => c.Type == "UserId" && c.Value == "1");
		token.Claims.Should().Contain(c => c.Type == "EmailVerified" && c.Value == "True");
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
}
