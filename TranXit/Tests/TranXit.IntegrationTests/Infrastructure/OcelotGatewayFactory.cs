extern alias OcelotApiGw;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using OcelotProgram = OcelotApiGw::Program;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class OcelotGatewayFactory : WebApplicationFactory<OcelotProgram>
{
	private readonly int _downstreamPort;

	public OcelotGatewayFactory(int downstreamPort)
	{
		_downstreamPort = downstreamPort;
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		TestConfiguration.ApplyToProcessEnvironment("unused");
		builder.UseEnvironment("Testing");
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			var currentConfiguration = configuration.Build();
			var rolesRoute = currentConfiguration
				.GetSection("Routes")
				.GetChildren()
				.Single(route => route["UpstreamPathTemplate"] == "/api/roles");
			var routePrefix = $"Routes:{rolesRoute.Key}:DownstreamHostAndPorts:0";
			var testConfiguration = TestConfiguration.ForService("unused");
			testConfiguration[$"{routePrefix}:Host"] = "127.0.0.1";
			testConfiguration[$"{routePrefix}:Port"] = _downstreamPort.ToString();
			configuration.AddInMemoryCollection(testConfiguration);
		});
	}
}
