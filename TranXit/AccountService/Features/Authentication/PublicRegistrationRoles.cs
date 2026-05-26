using AccountService.Database;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Features.Authentication;

internal static class PublicRegistrationRoles
{
	private static readonly string[] AllowedRoleNames = ["Customer", "Courier"];

	public static async Task<(Role? Role, string? Error)> ResolveAsync(
		AccountDbContext accountDbContext,
		string? roleName,
		int? roleId,
		CancellationToken cancellationToken)
	{
		var role = await FindRequestedRoleAsync(accountDbContext, roleName, roleId, cancellationToken);
		if (role is null)
		{
			return (null, "Role is invalid");
		}

		if (!AllowedRoleNames.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
		{
			return (null, "Public registration supports Customer or Courier only");
		}

		return (role, null);
	}

	public static bool HasRoleSelection(string? roleName, int? roleId)
		=> !string.IsNullOrWhiteSpace(roleName) || roleId is not null;

	public static bool IsPublicRole(Role? role)
		=> role is not null &&
		   AllowedRoleNames.Contains(role.Name, StringComparer.OrdinalIgnoreCase);

	private static async Task<Role?> FindRequestedRoleAsync(
		AccountDbContext accountDbContext,
		string? roleName,
		int? roleId,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(roleName))
		{
			var normalizedRoleName = roleName.Trim().ToLower();
			return await accountDbContext.Roles
				.SingleOrDefaultAsync(
					role => role.Name.ToLower() == normalizedRoleName,
					cancellationToken);
		}

		if (roleId is not null)
		{
			return await accountDbContext.Roles
				.SingleOrDefaultAsync(role => role.Id == roleId.Value, cancellationToken);
		}

		return null;
	}
}
