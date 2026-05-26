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
		builder.UseDefaultServiceProvider((_, options) =>
		{
			options.ValidateScopes = true;
			options.ValidateOnBuild = true;
		});
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(
				TestConfiguration.ForService(_fixture.CourierJobConnectionString));
		});
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			MassTransitTestTeardown.StopBus(Services);
		}

		try
		{
			base.Dispose(disposing);
		}
		catch (Exception exception) when (disposing && MassTransitTestTeardown.IsBenignTeardownRace(exception))
		{
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await MassTransitTestTeardown.StopBusAsync(Services);

		try
		{
			await base.DisposeAsync();
		}
		catch (Exception exception) when (MassTransitTestTeardown.IsBenignTeardownRace(exception))
		{
		}
	}
}
