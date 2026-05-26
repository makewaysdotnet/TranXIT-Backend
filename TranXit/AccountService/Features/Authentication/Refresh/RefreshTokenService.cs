using System.Security.Cryptography;
using AccountService.Database;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Features.Authentication.Refresh;

internal sealed record RefreshTokenIssue(string Token, DateTime ExpiresAtUtc, User User);

internal interface IRefreshTokenService
{
	Task<RefreshTokenIssue> IssueAsync(User user, CancellationToken cancellationToken);
	Task<RefreshTokenIssue?> RotateAsync(string refreshToken, CancellationToken cancellationToken);
}

internal sealed class RefreshTokenService(AccountDbContext accountDbContext, IConfiguration configuration)
	: IRefreshTokenService
{
	public async Task<RefreshTokenIssue> IssueAsync(User user, CancellationToken cancellationToken)
	{
		var secret = GenerateSecret();
		var refreshToken = new RefreshToken
		{
			UserId = user.Id,
			TokenHash = BC.EnhancedHashPassword(secret),
			CreatedAtUtc = DateTime.UtcNow,
			ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshExpiryDays)
		};

		await accountDbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
		await accountDbContext.SaveChangesAsync(cancellationToken);

		return new RefreshTokenIssue($"{refreshToken.Id}.{secret}", refreshToken.ExpiresAtUtc, user);
	}

	public async Task<RefreshTokenIssue?> RotateAsync(string refreshToken, CancellationToken cancellationToken)
	{
		var parsed = Parse(refreshToken);
		if (parsed is null)
		{
			return null;
		}

		var current = await accountDbContext.RefreshTokens
			.Include(x => x.User)
				.ThenInclude(user => user.Role)
			.SingleOrDefaultAsync(x => x.Id == parsed.Value.Id, cancellationToken);

		if (current is null ||
			current.RevokedAtUtc is not null ||
			current.ExpiresAtUtc <= DateTime.UtcNow ||
			!BC.EnhancedVerify(parsed.Value.Secret, current.TokenHash))
		{
			return null;
		}

		current.RevokedAtUtc = DateTime.UtcNow;
		var nextSecret = GenerateSecret();
		var next = new RefreshToken
		{
			UserId = current.UserId,
			TokenHash = BC.EnhancedHashPassword(nextSecret),
			CreatedAtUtc = DateTime.UtcNow,
			ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshExpiryDays)
		};

		accountDbContext.RefreshTokens.Update(current);
		await accountDbContext.RefreshTokens.AddAsync(next, cancellationToken);
		await accountDbContext.SaveChangesAsync(cancellationToken);

		return new RefreshTokenIssue($"{next.Id}.{nextSecret}", next.ExpiresAtUtc, current.User);
	}

	private int RefreshExpiryDays => int.TryParse(configuration["Jwt:RefreshExpiryDays"], out var days)
		? days
		: 14;

	private static string GenerateSecret()
	{
		var bytes = RandomNumberGenerator.GetBytes(32);
		return Convert.ToBase64String(bytes)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private static (int Id, string Secret)? Parse(string refreshToken)
	{
		var parts = refreshToken.Split('.', 2);
		if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || string.IsNullOrWhiteSpace(parts[1]))
		{
			return null;
		}

		return (id, parts[1]);
	}
}
