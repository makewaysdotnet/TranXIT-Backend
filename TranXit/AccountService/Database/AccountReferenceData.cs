namespace AccountService.Database;

public static class AccountReferenceData
{
	public const int CustomerRoleId = 1;
	public const int CourierRoleId = 2;
	public const int AgentRoleId = 3;
	public const int AdminRoleId = 4;

	public static Role[] CreateRoles() =>
	[
		new Role { Id = CustomerRoleId, Name = "Customer" },
		new Role { Id = CourierRoleId, Name = "Courier" },
		new Role { Id = AgentRoleId, Name = "Agent" },
		new Role { Id = AdminRoleId, Name = "Admin" }
	];
}
