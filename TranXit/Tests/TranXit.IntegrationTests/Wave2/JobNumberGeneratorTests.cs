using SharedServicesManager.Helpers;

namespace TranXit.IntegrationTests.Wave2;

public sealed class JobNumberGeneratorTests
{
	[Fact(DisplayName = "T-CUST-2.JobNumberSeedSpace")]
	public void JobNumbersAreNotLimitedToOneByteOfEntropy()
	{
		// UC-CUST-2, UC-NFR-9
		var numbers = Enumerable.Range(0, 1024)
			.Select(_ => new Utils().GenerateJobNumber())
			.ToArray();

		numbers.Should().OnlyContain(number =>
			number.Length == 8 && number.All(character =>
				"abcdefghijklmnopqrstuvwxyz0123456789".Contains(character)));
		numbers.Distinct(StringComparer.Ordinal).Count().Should().BeGreaterThan(256);
	}
}
