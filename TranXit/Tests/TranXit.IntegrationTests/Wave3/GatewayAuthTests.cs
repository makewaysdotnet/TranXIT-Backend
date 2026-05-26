using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave3;

public sealed class GatewayAuthTests : IAsyncLifetime
{
	private WebApplication? _downstream;
	private OcelotGatewayFactory? _gatewayFactory;
	private HttpClient? _gatewayClient;

	[Fact(DisplayName = "T-AUTH-7.GatewayUnauthenticated401")]
	public async Task GatewayUnauthenticated401()
	{
		// UC-AUTH-7, UC-NFR-3
		var response = await GatewayClient.GetAsync("/api/jobs/1/details");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-7.GatewayPublicAllowed200")]
	public async Task GatewayPublicAllowed200()
	{
		// UC-AUTH-7
		var response = await GatewayClient.GetAsync("/api/roles");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	public async Task InitializeAsync()
	{
		var port = GetFreePort();
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Testing"
		});
		builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");

		_downstream = builder.Build();
		_downstream.MapGet("/api/roles", () => Results.Json(new
		{
			isSuccess = true,
			value = Array.Empty<object>(),
			error = Array.Empty<string>()
		}));
		await _downstream.StartAsync();

		_gatewayFactory = new OcelotGatewayFactory(port);
		_gatewayClient = _gatewayFactory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	public async Task DisposeAsync()
	{
		_gatewayClient?.Dispose();
		_gatewayFactory?.Dispose();

		if (_downstream is not null)
		{
			await _downstream.StopAsync();
			await _downstream.DisposeAsync();
		}
	}

	private HttpClient GatewayClient => _gatewayClient
		?? throw new InvalidOperationException("Gateway client was not initialized.");

	private static int GetFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
