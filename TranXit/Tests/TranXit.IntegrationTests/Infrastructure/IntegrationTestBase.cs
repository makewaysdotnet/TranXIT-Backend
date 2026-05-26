using Microsoft.AspNetCore.Mvc.Testing;

namespace TranXit.IntegrationTests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(SqlContainerFixture fixture) : IAsyncLifetime
{
	private AccountServiceFactory? _accountFactory;
	private CourierJobServiceFactory? _courierJobFactory;

	protected SqlContainerFixture Fixture { get; } = fixture;
	protected TokenFactory Tokens { get; } = new();
	protected HttpClient AccountClient { get; private set; } = null!;
	protected HttpClient CourierClient { get; private set; } = null!;

	public async Task InitializeAsync()
	{
		await Fixture.ResetAsync();

		_accountFactory = new AccountServiceFactory(Fixture);
		_courierJobFactory = new CourierJobServiceFactory(Fixture);

		var clientOptions = new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		};
		AccountClient = _accountFactory.CreateClient(clientOptions);
		CourierClient = _courierJobFactory.CreateClient(clientOptions);
	}

	public Task DisposeAsync()
	{
		_accountFactory?.Dispose();
		_courierJobFactory?.Dispose();
		AccountClient.Dispose();
		CourierClient.Dispose();
		return Task.CompletedTask;
	}
}
