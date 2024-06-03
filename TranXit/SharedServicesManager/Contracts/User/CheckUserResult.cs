namespace SharedServicesManager.Contracts.User
{
	public record CheckUserResult
	{
		public int UserId { get; set; }
		public string? UserName { get; set; }
		public string? UserEmail { get; set; }
		public string? UserAddress { get; set; }
	}
}
