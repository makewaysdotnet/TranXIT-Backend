using System.Data;
using System.Security.Cryptography;
using AccountService.Database;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Features.Authentication.Refresh;

internal sealed record RefreshTokenIssue(string Token, DateTime ExpiresAtUtc, User User);

internal interface IRefreshTokenService
{
	Task<RefreshTokenIssue> IssueAsync(User user, CancellationToken cancellationToken);
	Task<RefreshTokenIssue?> RotateAsync(string refreshToken, CancellationToken cancellationToken);
	Task<bool> RevokeFamilyAsync(string? refreshToken, CancellationToken cancellationToken);
	Task<int> RevokeAllForUserAsync(int userId, string reason, CancellationToken cancellationToken);
}

internal sealed class RefreshTokenService(AccountDbContext accountDbContext, IConfiguration configuration)
	: IRefreshTokenService
{
	public async Task<RefreshTokenIssue> IssueAsync(User user, CancellationToken cancellationToken)
	{
		if (user.IsEmailVerified is not true)
		{
			throw new InvalidOperationException(
				"Refresh tokens cannot be issued before email verification.");
		}

		var secret = GenerateSecret();
		var refreshToken = new RefreshToken
		{
			UserId = user.Id,
			FamilyId = Guid.NewGuid(),
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

		await using var transaction = await accountDbContext.Database.BeginTransactionAsync(
			IsolationLevel.Serializable,
			cancellationToken);
		var current = await accountDbContext.RefreshTokens
			.FromSqlInterpolated(
				$"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {parsed.Value.Id}")
			.Include(x => x.User)
				.ThenInclude(user => user.Role)
			.SingleOrDefaultAsync(cancellationToken);

		if (current is null ||
			!BC.EnhancedVerify(parsed.Value.Secret, current.TokenHash))
		{
			return null;
		}

		if (current.RevokedAtUtc is not null)
		{
			await RevokeFamilyCoreAsync(
				current.FamilyId,
				"Refresh token reuse detected",
				cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return null;
		}

		if (current.ExpiresAtUtc <= DateTime.UtcNow ||
			current.User.IsEmailVerified is not true)
		{
			await RevokeFamilyCoreAsync(
				current.FamilyId,
				current.User.IsEmailVerified is true
					? "Refresh token expired"
					: "Email is not verified",
				cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return null;
		}

		current.RevokedAtUtc = DateTime.UtcNow;
		current.RevokedReason = "Rotated";
		var nextSecret = GenerateSecret();
		var next = new RefreshToken
		{
			UserId = current.UserId,
			FamilyId = current.FamilyId,
			ParentTokenId = current.Id,
			TokenHash = BC.EnhancedHashPassword(nextSecret),
			CreatedAtUtc = DateTime.UtcNow,
			ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshExpiryDays)
		};

		accountDbContext.RefreshTokens.Update(current);
		await accountDbContext.RefreshTokens.AddAsync(next, cancellationToken);
		await accountDbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return new RefreshTokenIssue($"{next.Id}.{nextSecret}", next.ExpiresAtUtc, current.User);
	}

	public async Task<bool> RevokeFamilyAsync(
		string? refreshToken,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			return false;
		}

		var parsed = Parse(refreshToken);
		if (parsed is null)
		{
			return false;
		}

		await using var transaction = await accountDbContext.Database.BeginTransactionAsync(
			IsolationLevel.Serializable,
			cancellationToken);
		var current = await accountDbContext.RefreshTokens
			.FromSqlInterpolated(
				$"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {parsed.Value.Id}")
			.SingleOrDefaultAsync(cancellationToken);

		if (current is null ||
			!BC.EnhancedVerify(parsed.Value.Secret, current.TokenHash))
		{
			return false;
		}

		await RevokeFamilyCoreAsync(
			current.FamilyId,
			"User logout",
			cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return true;
	}

	public Task<int> RevokeAllForUserAsync(
		int userId,
		string reason,
		CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		return accountDbContext.RefreshTokens
			.Where(token => token.UserId == userId && token.RevokedAtUtc == null)
			.ExecuteUpdateAsync(
				updates => updates
					.SetProperty(token => token.RevokedAtUtc, now)
					.SetProperty(token => token.RevokedReason, reason),
				cancellationToken);
	}

	private Task<int> RevokeFamilyCoreAsync(
		Guid familyId,
		string reason,
		CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		return accountDbContext.RefreshTokens
			.Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null)
			.ExecuteUpdateAsync(
				updates => updates
					.SetProperty(token => token.RevokedAtUtc, now)
					.SetProperty(token => token.RevokedReason, reason),
				cancellationToken);
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
