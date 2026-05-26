extern alias OcelotApiGw;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using OcelotProgram = OcelotApiGw::Program;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class OcelotGatewayFactory : WebApplicationFactory<OcelotProgram>
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		TestConfiguration.ApplyToProcessEnvironment("unused");
		builder.UseEnvironment("Testing");
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(TestConfiguration.ForService("unused"));
		});
	}
}
