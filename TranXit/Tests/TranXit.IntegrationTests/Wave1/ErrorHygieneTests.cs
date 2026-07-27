using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class ErrorHygieneTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-2.ErrorHygiene")]
	public async Task ErrorHygiene()
	{
		// UC-NFR-2
		var response = await CourierClient.GetAsync("/api/test/error");

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		var body = await response.ReadBodyAsync();
		body.Should().Contain("correlationId");
		body.Should().NotContain("InvalidOperationException");
		body.Should().NotContain("Sensitive test exception");
		body.Should().NotContain(" at ");
	}
}
