using Microsoft.EntityFrameworkCore;

namespace AccountService.Database;

public static class AccountDevelopmentSeeder
{
	private const string CustomerEmail = "customer@tranxit.local";
	private const string CourierEmail = "courier@tranxit.local";
	private const string AdminEmail = "admin@tranxit.local";
	private const string DemoPassword = "Password1!";

	public static async Task SeedAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();

		var customerRole = await db.Roles.SingleAsync(r => r.Name == "Customer");
		var courierRole = await db.Roles.SingleAsync(r => r.Name == "Courier");
		var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin");

		if (!await db.Users.AnyAsync(u => u.Email == CustomerEmail))
		{
			db.Users.Add(new User
			{
				Email = CustomerEmail,
				Username = "Ayesha Khan",
				Phone = "+92 300 0000001",
				RoleId = customerRole.Id,
				PasswordHash = BC.EnhancedHashPassword(DemoPassword),
				IsEmailVerified = true
			});
		}

		if (!await db.Users.AnyAsync(u => u.Email == CourierEmail))
		{
			db.Users.Add(new User
			{
				Email = CourierEmail,
				Username = "TranXIT Courier Co.",
				Phone = "+92 300 0000002",
				RoleId = courierRole.Id,
				PasswordHash = BC.EnhancedHashPassword(DemoPassword),
				IsEmailVerified = true
			});
		}

		if (!await db.Users.AnyAsync(u => u.Email == AdminEmail))
		{
			db.Users.Add(new User
			{
				Email = AdminEmail,
				Username = "TranXIT Admin",
				Phone = "+92 300 0000003",
				RoleId = adminRole.Id,
				PasswordHash = BC.EnhancedHashPassword(DemoPassword),
				IsEmailVerified = true
			});
		}

		await db.SaveChangesAsync();
	}

}
