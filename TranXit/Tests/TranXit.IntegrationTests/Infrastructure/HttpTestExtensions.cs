namespace TranXit.IntegrationTests.Infrastructure;

internal static class HttpTestExtensions
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	public static void AuthenticateAs(this HttpClient client, string token)
		=> client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

	public static async Task<ApiResult<T>> ReadApiResultAsync<T>(this HttpResponseMessage response)
	{
		var result = await response.Content.ReadFromJsonAsync<ApiResult<T>>(JsonOptions);
		result.Should().NotBeNull();
		return result!;
	}

	public static async Task<string> ReadBodyAsync(this HttpResponseMessage response)
		=> await response.Content.ReadAsStringAsync();
}
