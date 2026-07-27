using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Database;

public static class AccountProductionAdminBootstrapper
{
	public static async Task BootstrapAsync(
		IServiceProvider services,
		IConfiguration configuration,
		CancellationToken cancellationToken = default)
	{
		var email = configuration["AdminBootstrap:Email"]?.Trim();
		if (string.IsNullOrWhiteSpace(email))
		{
			throw new InvalidOperationException(
				"AdminBootstrap:Email is required for --bootstrap-admin.");
		}

		try
		{
			_ = new MailAddress(email);
		}
		catch (FormatException exception)
		{
			throw new InvalidOperationException(
				"AdminBootstrap:Email must be a valid email address.",
				exception);
		}

		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
		await using var transaction = await db.Database.BeginTransactionAsync(
			IsolationLevel.Serializable,
			cancellationToken);

		var adminRole = await db.Roles.SingleAsync(
			role => role.Name == "Admin",
			cancellationToken);
		var admins = await db.Users
			.Where(user => user.RoleId == adminRole.Id)
			.ToListAsync(cancellationToken);

		if (admins.Count > 1)
		{
			throw new InvalidOperationException(
				"Admin bootstrap refused because more than one Admin already exists.");
		}

		if (admins.Count == 1)
		{
			if (!string.Equals(
				admins[0].Email,
				email,
				StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					"Admin bootstrap refused because a different Admin already exists.");
			}

			await transaction.CommitAsync(cancellationToken);
			return;
		}

		if (await db.Users.AnyAsync(
			user => user.Email == email,
			cancellationToken))
		{
			throw new InvalidOperationException(
				"Admin bootstrap refused because the configured email belongs to a non-Admin user.");
		}

		var unknownPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		db.Users.Add(new User
		{
			Email = email,
			Username = "TranXIT Admin",
			RoleId = adminRole.Id,
			PasswordHash = BC.EnhancedHashPassword(unknownPassword),
			IsEmailVerified = true
		});

		await db.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		Console.WriteLine(
			"Admin account created without a usable initial password. Complete the forgot-password flow before first login.");
	}
}
