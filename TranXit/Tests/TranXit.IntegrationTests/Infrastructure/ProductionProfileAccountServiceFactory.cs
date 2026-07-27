extern alias AccountService;

using AccountProgram = AccountService::Program;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedServicesManager.Helpers;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class ProductionProfileAccountServiceFactory
	: WebApplicationFactory<AccountProgram>
{
	private readonly string _connectionString;

	public ProductionProfileAccountServiceFactory(string connectionString)
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
		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IUtils>();
			services.AddScoped<IUtils, DeterministicUtils>();
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
