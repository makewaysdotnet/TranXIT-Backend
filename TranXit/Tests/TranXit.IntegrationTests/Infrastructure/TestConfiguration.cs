namespace TranXit.IntegrationTests.Infrastructure;

internal static class TestConfiguration
{
	public const string Issuer = "TranXIT.AccountService";
	public const string Audience = "TranXIT.ApiClients";
	public const string SigningKey = "TranXIT.integration.tests.jwt.signing.key.2026";
	public const double ExpiryMinutes = 60;

	public static Dictionary<string, string?> ForService(string connectionString)
		=> new()
		{
			["ConnectionStrings:Database"] = connectionString,
			["AllowedOrigins:0"] = "http://localhost:3000",
			["Jwt:Issuer"] = Issuer,
			["Jwt:Audience"] = Audience,
			["Jwt:ExpiryMinutes"] = ExpiryMinutes.ToString(),
			["Jwt:RequireHttpsMetadata"] = "false",
			["SharedJwtSecrets:Key"] = SigningKey,
			["MailSettings:DisableSending"] = "true",
			["RabbitMQ:HostName"] = "unused-in-testing",
			["RabbitMQ:UserName"] = "unused-in-testing",
			["RabbitMQ:Password"] = "unused-in-testing"
		};

	public static void ApplyToProcessEnvironment(string connectionString)
	{
		foreach (var (key, value) in ForService(connectionString))
		{
			Environment.SetEnvironmentVariable(key.Replace(':', '_').Replace("_", "__"), value);
		}

		Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
		Environment.SetEnvironmentVariable("ConnectionStrings__Database", connectionString);
		Environment.SetEnvironmentVariable("AllowedOrigins__0", "http://localhost:3000");
		Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
		Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
		Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", ExpiryMinutes.ToString());
		Environment.SetEnvironmentVariable("Jwt__RequireHttpsMetadata", "false");
		Environment.SetEnvironmentVariable("SharedJwtSecrets__Key", SigningKey);
		Environment.SetEnvironmentVariable("MailSettings__DisableSending", "true");
		Environment.SetEnvironmentVariable("RabbitMQ__HostName", "unused-in-testing");
		Environment.SetEnvironmentVariable("RabbitMQ__UserName", "unused-in-testing");
		Environment.SetEnvironmentVariable("RabbitMQ__Password", "unused-in-testing");
	}
}
