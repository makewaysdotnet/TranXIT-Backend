extern alias AccountService;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using AccountProgram = AccountService::Program;
using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class AccountServiceFactory : WebApplicationFactory<AccountProgram>
{
	private readonly SqlContainerFixture _fixture;
	private readonly DbCommandInterceptor? _interceptor;

	public AccountServiceFactory(SqlContainerFixture fixture, DbCommandInterceptor? interceptor = null)
	{
		_fixture = fixture;
		_interceptor = interceptor;
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
		if (_interceptor is not null)
		{
			builder.ConfigureTestServices(services =>
			{
				// EF Core 8 retains the first options registration; replace it explicitly.
				services.RemoveAll<DbContextOptions<AccountDbContext>>();
				services.AddDbContext<AccountDbContext>(options => options
					.UseSqlServer(_fixture.AccountConnectionString).AddInterceptors(_interceptor));
			});
		}
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
