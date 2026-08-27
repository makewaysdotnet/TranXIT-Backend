extern alias CourierJobService;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedServicesManager.Helpers;
using System.Text.Json;
using TranXit.IntegrationTests.Infrastructure;

using CourierJobProgram = CourierJobService::Program;

namespace TranXit.IntegrationTests.Wave2;

[Collection(IntegrationTestCollection.Name)]
public sealed class CustomerJobNumberTests(SqlContainerFixture fixture) : IAsyncLifetime
{
	private JobNumberFactory? _factory;
	private HttpClient? _client;

	public Task InitializeAsync() => fixture.ResetAsync();

	public async Task DisposeAsync()
	{
		if (_factory is not null)
		{
			await _factory.DisposeAsync();
		}
		_client?.Dispose();
	}

	[Fact(DisplayName = "T-CUST-2.JobNumberCollisionRetries")]
	public async Task CollisionRetriesWithoutDuplicatingTheJobOrItems()
	{
		// UC-CUST-2, UC-NFR-9
		var numbers = new ScriptedNumbers("TX1001", "fresh002");
		var client = CreateClient(numbers);

		var response = await client.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload());

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var result = await response.ReadApiResultAsync<JobValue>();
		result.IsSuccess.Should().BeTrue();
		numbers.Calls.Should().Be(2);
		await using var db = fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.Include(value => value.JobItems).SingleAsync(value => value.Id == result.Value!.JobId);
		job.JobNumber.Should().Be("fresh002");
		job.UserId.Should().Be(1);
		job.JobItems.Should().ContainSingle(value => value.Name == "Test cartons");
		(await db.Jobs.CountAsync()).Should().Be(2);
		(await db.JobItems.CountAsync()).Should().Be(2);
	}

	[Fact(DisplayName = "T-CUST-2.JobNumberCollisionExhaustion400")]
	public async Task RepeatedCollisionsFailCleanlyWithoutAnyPartialWrite()
	{
		// UC-CUST-2, UC-NFR-9
		var numbers = new ScriptedNumbers("TX1001");
		var client = CreateClient(numbers);

		var response = await client.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload());

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<JobValue>();
		result.IsSuccess.Should().BeFalse();
		numbers.Calls.Should().Be(3);
		await using var db = fixture.CreateCourierJobDbContext();
		(await db.Jobs.CountAsync()).Should().Be(1);
		(await db.JobItems.CountAsync()).Should().Be(1);
	}

	[Fact(DisplayName = "T-CUST-2.JobNumberConcurrentCollision")]
	public async Task TwoRequestsWithTheSameNumberBothPersistExactlyOnce()
	{
		// UC-CUST-2, UC-NFR-9
		var numbers = new ScriptedNumbers("same0001", "same0001", "next0001");
		var client = CreateClient(numbers);

		var responses = await Task.WhenAll(
			client.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload()),
			client.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload()));

		responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Created);
		var results = await Task.WhenAll(responses.Select(response => response.ReadApiResultAsync<JobValue>()));
		results.Should().OnlyContain(result => result.IsSuccess);
		results.Select(result => result.Value!.JobId).Should().OnlyHaveUniqueItems();
		numbers.Calls.Should().Be(3);
		await using var db = fixture.CreateCourierJobDbContext();
		(await db.Jobs.CountAsync()).Should().Be(3);
		(await db.JobItems.CountAsync()).Should().Be(3);
		(await db.Jobs.Where(job => job.Id != 1).Select(job => job.JobNumber).ToArrayAsync())
			.Should().BeEquivalentTo("same0001", "next0001");
	}

	[Fact(DisplayName = "T-CUST-2.JobNumberDoesNotRetryOtherSqlErrors")]
	public async Task OtherSqlConstraintFailuresAreNotTreatedAsNumberCollisions()
	{
		// UC-CUST-2, UC-NFR-2
		var numbers = new ScriptedNumbers("fresh003");
		var client = CreateClient(numbers);
		var payload = JsonSerializer.SerializeToNode(CustomerJobTests.CreateJobPayload())!.AsObject();
		payload["cargoModeId"] = int.MaxValue;

		var response = await client.PostAsJsonAsync("/api/jobs", payload);

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		numbers.Calls.Should().Be(1);
		(await response.Content.ReadAsStringAsync()).Should().NotContain("FOREIGN KEY");
		await using var db = fixture.CreateCourierJobDbContext();
		(await db.Jobs.CountAsync()).Should().Be(1);
		(await db.JobItems.CountAsync()).Should().Be(1);
	}

	private HttpClient CreateClient(IUtils numbers)
	{
		_factory = new JobNumberFactory(fixture, numbers);
		_client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		_client.AuthenticateAs(new TokenFactory().ForUser(1, "Customer"));
		return _client;
	}

	private sealed class ScriptedNumbers(params string[] values) : IUtils
	{
		private int _calls;
		public int Calls => Volatile.Read(ref _calls);
		public int Generate6DRandomCode() => throw new InvalidOperationException("Job creation must not generate an OTP.");
		public string GenerateJobNumber() => values[Math.Min(Interlocked.Increment(ref _calls) - 1, values.Length - 1)];
	}

	private sealed class JobNumberFactory : WebApplicationFactory<CourierJobProgram>
	{
		private readonly SqlContainerFixture _fixture;
		private readonly IUtils _numbers;
		private IServiceProvider? _hostServices;

		public JobNumberFactory(SqlContainerFixture fixture, IUtils numbers)
		{
			_fixture = fixture;
			_numbers = numbers;
			TestConfiguration.ApplyToProcessEnvironment(fixture.CourierJobConnectionString);
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
				configuration.AddInMemoryCollection(TestConfiguration.ForService(_fixture.CourierJobConnectionString)));
			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<IUtils>();
				services.AddSingleton(_numbers);
			});
		}

		protected override IHost CreateHost(IHostBuilder builder)
		{
			var host = base.CreateHost(builder);
			_hostServices = host.Services;
			return host;
		}

		public override async ValueTask DisposeAsync()
		{
			if (_hostServices is not null)
			{
				await MassTransitTestTeardown.StopBusAsync(_hostServices);
			}
			try
			{
				await base.DisposeAsync();
			}
			catch (Exception exception) when (MassTransitTestTeardown.IsBenignTeardownRace(exception))
			{
			}
		}
	}
}
