extern alias AccountService;
extern alias CourierJobService;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DotNet.Testcontainers.Images;
using Respawn;
using Testcontainers.MsSql;

using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;
using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;

namespace TranXit.IntegrationTests.Infrastructure;

public sealed class SqlContainerFixture : IAsyncLifetime
{
	private const string SqlPassword = "TranXIT_Test_2026!";
	private const string AccountDatabase = "TranXit_Account_Integration";
	private const string CourierJobDatabase = "TranXit_CourierJob_Integration";
	private readonly SemaphoreSlim _resetLock = new(1, 1);
	private readonly MsSqlContainer _container = new MsSqlBuilder()
		.WithImage("mcr.microsoft.com/mssql/server:2022-latest")
		.WithImagePullPolicy(PullPolicy.Missing)
		.WithPassword(SqlPassword)
		.Build();
	private Respawner? _accountRespawner;
	private Respawner? _courierJobRespawner;

	public string AccountConnectionString { get; private set; } = string.Empty;
	public string CourierJobConnectionString { get; private set; } = string.Empty;

	public async Task InitializeAsync()
	{
		await _container.StartAsync();
		AccountConnectionString = BuildConnectionString(AccountDatabase);
		CourierJobConnectionString = BuildConnectionString(CourierJobDatabase);

		await EnsureSchemasAsync();
		_accountRespawner = await CreateRespawnerAsync(AccountConnectionString);
		_courierJobRespawner = await CreateRespawnerAsync(CourierJobConnectionString);
		await ResetAsync();
	}

	public async Task DisposeAsync()
	{
		_resetLock.Dispose();
		await _container.DisposeAsync();
	}

	public async Task ResetAsync()
	{
		if (_accountRespawner is null || _courierJobRespawner is null)
		{
			return;
		}

		await _resetLock.WaitAsync();
		try
		{
			await using var accountConnection = new SqlConnection(AccountConnectionString);
			await accountConnection.OpenAsync();
			await _accountRespawner.ResetAsync(accountConnection);

			await using var courierJobConnection = new SqlConnection(CourierJobConnectionString);
			await courierJobConnection.OpenAsync();
			await _courierJobRespawner.ResetAsync(courierJobConnection);

			await TestSeed.SeedAccountAsync(AccountConnectionString);
			await TestSeed.SeedCourierJobAsync(CourierJobConnectionString);
		}
		finally
		{
			_resetLock.Release();
		}
	}

	public AccountDbContext CreateAccountDbContext()
	{
		var options = new DbContextOptionsBuilder<AccountDbContext>()
			.UseSqlServer(AccountConnectionString)
			.Options;
		return new AccountDbContext(options);
	}

	public CourierJobDbContext CreateCourierJobDbContext()
	{
		var options = new DbContextOptionsBuilder<CourierJobDbContext>()
			.UseSqlServer(CourierJobConnectionString)
			.Options;
		return new CourierJobDbContext(options);
	}

	public string BuildTemporaryConnectionString(string databasePrefix)
	{
		var safePrefix = databasePrefix.Replace('-', '_');
		return BuildConnectionString($"{safePrefix}_{Guid.NewGuid():N}");
	}

	private async Task EnsureSchemasAsync()
	{
		await using (var accountDb = CreateAccountDbContext())
		{
			await accountDb.Database.EnsureCreatedAsync();
		}

		await using (var courierJobDb = CreateCourierJobDbContext())
		{
			await courierJobDb.Database.EnsureCreatedAsync();
		}
	}

	private async Task<Respawner> CreateRespawnerAsync(string connectionString)
	{
		await using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();
		return await Respawner.CreateAsync(connection, new RespawnerOptions
		{
			DbAdapter = DbAdapter.SqlServer,
			SchemasToInclude = ["dbo"]
		});
	}

	private string BuildConnectionString(string database)
	{
		var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
		{
			InitialCatalog = database,
			TrustServerCertificate = true,
			Encrypt = false,
			MultipleActiveResultSets = true
		};
		return builder.ConnectionString;
	}
}
