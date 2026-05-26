extern alias CourierJobService;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using CourierJobProgram = CourierJobService::Program;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class CourierJobServiceFactory : WebApplicationFactory<CourierJobProgram>
{
	private readonly SqlContainerFixture _fixture;

	public CourierJobServiceFactory(SqlContainerFixture fixture)
	{
		_fixture = fixture;
		TestConfiguration.ApplyToProcessEnvironment(_fixture.CourierJobConnectionString);
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		TestConfiguration.ApplyToProcessEnvironment(_fixture.CourierJobConnectionString);
		builder.UseEnvironment("Testing");
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(
				TestConfiguration.ForService(_fixture.CourierJobConnectionString));
		});
	}
}
