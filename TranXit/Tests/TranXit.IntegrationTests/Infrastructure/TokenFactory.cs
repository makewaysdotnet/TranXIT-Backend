extern alias AccountService;

using JwtTokenBuilder = AccountService::AccountService.Features.Authentication.TokenManager.JwtTokenBuilder;
using TokenBuilderRequest = AccountService::AccountService.Features.Authentication.TokenManager.TokenBuilderRequest;

namespace TranXit.IntegrationTests.Infrastructure;

public sealed class TokenFactory
{
	private readonly JwtTokenBuilder _jwtTokenBuilder = new();

	public string ForUser(
		int userId,
		string role,
		string? email = null,
		bool emailVerified = true,
		string? issuer = null,
		string? audience = null,
		double? expiryMinutes = null)
		=> _jwtTokenBuilder.BuildToken(new TokenBuilderRequest
		{
			UserId = userId.ToString(),
			Username = $"{role} {userId}",
			Role = role,
			Email = email ?? $"{role.ToLowerInvariant()}.{userId}@tranxit.test",
			EmailVerified = emailVerified,
			SecretKey = TestConfiguration.SigningKey,
			Issuer = issuer ?? TestConfiguration.Issuer,
			Audience = audience ?? TestConfiguration.Audience,
			ExpiryMinutes = expiryMinutes ?? TestConfiguration.ExpiryMinutes
		});
}
