using SharedServicesManager.Helpers;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed class DeterministicUtils : IUtils
{
	public const string VerificationCode = "123456";

	public int Generate6DRandomCode() => int.Parse(VerificationCode);

	public string GenerateJobNumber() => "prod0001";
}
