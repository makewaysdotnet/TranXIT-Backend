using Microsoft.EntityFrameworkCore;

namespace CourierJobService.Database;

public static class CourierJobDatabaseMigrator
{
	public static async Task MigrateAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<CourierJobDbContext>();

		for (var attempt = 1; attempt <= 30; attempt++)
		{
			try
			{
				await db.Database.MigrateAsync();
				return;
			}
			catch when (attempt < 30)
			{
				// SQL Server can take a moment to accept logins after the container starts.
			}

			await Task.Delay(TimeSpan.FromSeconds(2));
		}
	}
}
