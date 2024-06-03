namespace SharedServicesManager.Contracts.User
{
	public record CheckUser
	{
		public int UserId { get; set; }
		public string Username { get; set; } = string.Empty;
	}
}
