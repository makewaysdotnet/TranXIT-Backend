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
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(
				TestConfiguration.ForService(_fixture.AccountConnectionString));
		});
	}
}
