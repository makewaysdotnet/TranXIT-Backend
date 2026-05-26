extern alias AccountService;

using JwtTokenBuilder = AccountService::AccountService.Features.Authentication.TokenManager.JwtTokenBuilder;
using TokenBuilderRequest = AccountService::AccountService.Features.Authentication.TokenManager.TokenBuilderRequest;

namespace TranXit.IntegrationTests.Infrastructure;

public sealed class TokenFactory
{
	private readonly JwtTokenBuilder _jwtTokenBuilder = new();

	public string ForUser(int userId, string role, string? email = null, bool emailVerified = true)
		=> _jwtTokenBuilder.BuildToken(new TokenBuilderRequest
		{
			UserId = userId.ToString(),
			Username = $"{role} {userId}",
			Role = role,
			Email = email ?? $"{role.ToLowerInvariant()}.{userId}@tranxit.test",
			EmailVerified = emailVerified,
			SecretKey = TestConfiguration.SigningKey,
			Issuer = TestConfiguration.Issuer,
			Audience = TestConfiguration.Audience,
			ExpiryMinutes = TestConfiguration.ExpiryMinutes
		});
}
