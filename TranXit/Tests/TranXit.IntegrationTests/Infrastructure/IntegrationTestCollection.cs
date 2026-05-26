namespace TranXit.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<SqlContainerFixture>
{
	public const string Name = "TranXIT integration collection";
}
