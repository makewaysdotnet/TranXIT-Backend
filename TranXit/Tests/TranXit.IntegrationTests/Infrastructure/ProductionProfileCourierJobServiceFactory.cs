extern alias CourierJobService;

using CourierJobProgram = CourierJobService::Program;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class ProductionProfileCourierJobServiceFactory
	: WebApplicationFactory<CourierJobProgram>
{
	private readonly string _connectionString;

	public ProductionProfileCourierJobServiceFactory(string connectionString)
	{
		_connectionString = connectionString;
		TestConfiguration.ApplyToProcessEnvironment(connectionString, "Production");
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Production");
		builder.UseDefaultServiceProvider((_, options) =>
		{
			options.ValidateScopes = true;
			options.ValidateOnBuild = true;
		});
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			var values = TestConfiguration.ForService(_connectionString);
			values["TestInfrastructure:UseInMemoryBus"] = "true";
			configuration.AddInMemoryCollection(values);
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
