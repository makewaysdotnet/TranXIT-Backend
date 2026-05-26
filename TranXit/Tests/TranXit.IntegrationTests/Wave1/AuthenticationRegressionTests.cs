using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class AuthenticationRegressionTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-AUTH-1.AdminRoleName")]
	public async Task AdminRoleName()
	{
		// UC-AUTH-1, E2E-1
		var response = await AccountClient.PostAsJsonAsync("/api/register", RegisterPayload(
			email: "admin-role-name@tranxit.test",
			role: "Admin"));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().BeNull();
	}

	[Fact(DisplayName = "T-AUTH-6.LegacyAdminRoleId")]
	public async Task LegacyAdminRoleId()
	{
		// UC-AUTH-6, E2E-1
		var response = await AccountClient.PostAsJsonAsync("/api/register", RegisterPayload(
			email: "legacy-admin-role-id@tranxit.test",
			roleId: 4));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().BeNull();
	}

	[Fact(DisplayName = "T-AUTH-6.UnknownRole")]
	public async Task UnknownRole()
	{
		// UC-AUTH-6
		var response = await AccountClient.PostAsJsonAsync("/api/register", RegisterPayload(
			email: "unknown-role@tranxit.test",
			role: "Ghost"));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().BeNull();
	}

	[Fact(DisplayName = "T-AUTH-1.RegisterCustomer.Happy")]
	public async Task RegisterCustomerHappy()
	{
		// UC-AUTH-1
		var response = await AccountClient.PostAsJsonAsync("/api/register", RegisterPayload(
			email: "new-customer@tranxit.test",
			role: "Customer"));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.Role.Should().Be("Customer");
		result.Value.RoleId.Should().Be(1);

		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(result.Value.Id);
		user.Should().NotBeNull();
		user!.Email.Should().Be("new-customer@tranxit.test");
		user.RoleId.Should().Be(1);
	}

	[Fact(DisplayName = "T-AUTH-6.GoogleLoginAdminBlocked")]
	public async Task GoogleLoginAdminBlocked()
	{
		// UC-AUTH-6
		var response = await AccountClient.PostAsJsonAsync("/api/login/google", new
		{
			name = "Blocked Admin",
			email = "google-admin@tranxit.test",
			image = "",
			role = "Admin",
			phone = "+920000000009",
			provider = 0
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<LoginValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().BeNull();
	}

	private static object RegisterPayload(string email, string? role = null, int? roleId = null)
		=> new
		{
			email,
			password = "Password1!",
			confirmPassword = "Password1!",
			username = "Integration User",
			phone = "+920000000099",
			role,
			roleId
		};
}
