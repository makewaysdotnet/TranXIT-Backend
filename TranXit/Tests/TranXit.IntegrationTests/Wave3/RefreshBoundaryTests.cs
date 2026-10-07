using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave3;

public sealed class RefreshBoundaryTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-AUTH-10.LogoutAmbientCookieRejected")]
	public async Task LogoutAmbientCookieRejected()
	{
		// UC-AUTH-10, UC-NFR-7: browser cookies alone must not authorize backend logout.
		var login = await LoginAsync();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
		request.Headers.Add("Cookie", $"tranxit_refresh={login.RefreshToken}");
		using var response = await AccountClient.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await response.ReadApiResultAsync<bool>()).IsSuccess.Should().BeFalse();
		using var refresh = await AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken = login.RefreshToken });
		refresh.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact(DisplayName = "T-AUTH-10.LogoutCookieCannotOverrideBody")]
	public async Task LogoutCookieCannotOverrideBody()
	{
		var login = await LoginAsync();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout")
		{
			Content = JsonContent.Create(new { refreshToken = "invalid-test-credential" })
		};
		request.Headers.Add("Cookie", $"tranxit_refresh={login.RefreshToken}");
		using var response = await AccountClient.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		using var refresh = await AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken = login.RefreshToken });
		refresh.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact(DisplayName = "T-AUTH-10.LogoutExplicitCredentialIdempotentTokenFree")]
	public async Task LogoutExplicitCredentialIdempotentTokenFree()
	{
		var login = await LoginAsync();
		for (var attempt = 0; attempt < 2; attempt++)
		{
			using var response = await AccountClient.PostAsJsonAsync("/api/logout", new { refreshToken = login.RefreshToken });
			response.StatusCode.Should().Be(HttpStatusCode.OK);
			var json = await response.Content.ReadAsStringAsync();
			json.Should().NotContain(login.RefreshToken!).And.NotContain(login.Token!);
			var result = await response.ReadApiResultAsync<bool>();
			result.IsSuccess.Should().BeTrue();
			result.Value.Should().BeTrue();
		}
		using var refresh = await AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken = login.RefreshToken });
		refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-10.RefreshAmbientCookieRejected")]
	public async Task RefreshAmbientCookieRejected()
	{
		// UC-AUTH-10, UC-NFR-7
		var login = await LoginAsync();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/refresh");
		request.Headers.Add("Cookie", $"tranxit_refresh={login.RefreshToken}");

		var response = await AccountClient.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		var denied = await response.ReadApiResultAsync<LoginValue>();
		denied.IsSuccess.Should().BeFalse();
		denied.Value.Should().BeNull();
		var explicitRefresh = await AccountClient.PostAsJsonAsync("/api/refresh", new
		{
			refreshToken = login.RefreshToken
		});
		explicitRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
		var rotated = await explicitRefresh.ReadApiResultAsync<LoginValue>();
		rotated.Value!.RefreshToken.Should().NotBe(login.RefreshToken);
	}

	[Fact(DisplayName = "T-AUTH-10.RefreshCookieCannotOverrideBody")]
	public async Task RefreshCookieCannotOverrideBody()
	{
		// UC-AUTH-10, UC-NFR-7
		var login = await LoginAsync();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/refresh")
		{
			Content = JsonContent.Create(new { refreshToken = "invalid-test-credential" })
		};
		request.Headers.Add("Cookie", $"tranxit_refresh={login.RefreshToken}");

		var response = await AccountClient.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		var denied = await response.ReadApiResultAsync<LoginValue>();
		denied.Value.Should().BeNull();
		var explicitRefresh = await AccountClient.PostAsJsonAsync("/api/refresh", new
		{
			refreshToken = login.RefreshToken
		});
		explicitRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact(DisplayName = "T-AUTH-10.RefreshMissingCredential401")]
	public async Task RefreshMissingCredential401()
	{
		// UC-AUTH-10, UC-NFR-7
		var response = await AccountClient.PostAsJsonAsync("/api/refresh", new { });

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		var denied = await response.ReadApiResultAsync<LoginValue>();
		denied.IsSuccess.Should().BeFalse();
		denied.Value.Should().BeNull();
	}

	private async Task<LoginValue> LoginAsync()
	{
		var response = await AccountClient.PostAsJsonAsync("/api/login", new
		{
			email = "customer.seed@tranxit.test",
			password = "Password1!"
		});
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		return (await response.ReadApiResultAsync<LoginValue>()).Value!;
	}
}
