using System.Text.Json.Serialization;

namespace TranXit.IntegrationTests.Infrastructure;

internal sealed record ApiResult<T>
{
	[JsonPropertyName("isSuccess")]
	public bool IsSuccess { get; init; }

	[JsonPropertyName("value")]
	public T? Value { get; init; }

	[JsonPropertyName("error")]
	public string[] Error { get; init; } = [];
}

internal sealed record LoginValue(
	[property: JsonPropertyName("id")] int Id,
	[property: JsonPropertyName("email")] string? Email,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("roleId")] int? RoleId,
	[property: JsonPropertyName("role")] string? Role,
	[property: JsonPropertyName("developmentVerificationCode")] string? DevelopmentVerificationCode);

internal sealed record BidValue(
	[property: JsonPropertyName("bidId")] int BidId);

internal sealed record UserValue(
	[property: JsonPropertyName("id")] int Id,
	[property: JsonPropertyName("email")] string? Email,
	[property: JsonPropertyName("username")] string? Username,
	[property: JsonPropertyName("roleId")] int? RoleId);
