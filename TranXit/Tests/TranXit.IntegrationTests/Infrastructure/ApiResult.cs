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
	[property: JsonPropertyName("isEmailVerified")] bool IsEmailVerified,
	[property: JsonPropertyName("token")] string? Token,
	[property: JsonPropertyName("refreshToken")] string? RefreshToken,
	[property: JsonPropertyName("refreshTokenExpires")] string? RefreshTokenExpires,
	[property: JsonPropertyName("expires")] string? Expires,
	[property: JsonPropertyName("developmentVerificationCode")] string? DevelopmentVerificationCode);

internal sealed record BidValue(
	[property: JsonPropertyName("bidId")] int BidId);

internal sealed record JobValue(
	[property: JsonPropertyName("jobId")] int JobId);

internal sealed record JobDetailValue(
	[property: JsonPropertyName("jobId")] int JobId,
	[property: JsonPropertyName("userId")] int UserId,
	[property: JsonPropertyName("customerName")] string? CustomerName,
	[property: JsonPropertyName("jobNumber")] string? JobNumber);

internal sealed record ImageValue(
	[property: JsonPropertyName("id")] int? Id,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("type")] string? Type,
	[property: JsonPropertyName("content")] string? Content);

internal sealed record UserValue(
	[property: JsonPropertyName("id")] int Id,
	[property: JsonPropertyName("email")] string? Email,
	[property: JsonPropertyName("username")] string? Username,
	[property: JsonPropertyName("roleId")] int? RoleId);
