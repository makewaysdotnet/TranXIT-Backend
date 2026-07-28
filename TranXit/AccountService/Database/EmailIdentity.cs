using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Database;

public static class EmailIdentity
{
	public static string Normalize(string email) =>
		email.Trim().ToLowerInvariant();

	public static bool IsUniqueViolation(DbUpdateException exception) =>
		exception.InnerException is SqlException sqlException &&
		sqlException.Number is 2601 or 2627;
}
