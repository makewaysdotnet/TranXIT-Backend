extern alias AccountService;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using AccountProgram = AccountService::Program;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class AccountServiceFactory : WebApplicationFactory<AccountProgram>
{
	private readonly SqlContainerFixture _fixture;

	public AccountServiceFactory(SqlContainerFixture fixture)
	{
		_fixture = fixture;
		TestConfiguration.ApplyToProcessEnvironment(_fixture.AccountConnectionString);
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		TestConfiguration.ApplyToProcessEnvironment(_fixture.AccountConnectionString);
		builder.UseEnvironment("Testing");
		builder.UseDefaultServiceProvider((_, options) =>
		{
			options.ValidateScopes = true;
			options.ValidateOnBuild = true;
		});
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(
				TestConfiguration.ForService(_fixture.AccountConnectionString));
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
