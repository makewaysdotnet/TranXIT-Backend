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
		Environment.SetEnvironmentVariable("Routes__3__DownstreamHostAndPorts__0__Host", "127.0.0.1");
		Environment.SetEnvironmentVariable("Routes__3__DownstreamHostAndPorts__0__Port", _downstreamPort.ToString());
		builder.UseEnvironment("Testing");
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(TestConfiguration.ForService("unused"));
		});
	}
}
