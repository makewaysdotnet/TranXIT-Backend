using AccountService.Database;
using MassTransit;
using SharedServicesManager.Contracts.User;

namespace AccountService.Consumers
{
	public class UserConsumer(AccountDbContext accountDbContext) : IConsumer<CheckUser>
	{
		public async Task Consume(ConsumeContext<CheckUser> context)
		{
			var userResponse = await accountDbContext.Users
				.FindAsync(context.Message.UserId);
			if (userResponse == null)
			{
				throw new InvalidOperationException("User not found");
			}
			await context.RespondAsync(new CheckUserResult
			{
				UserId = userResponse.Id,
				UserEmail = userResponse.Email,
				UserName = userResponse.Username
			});
		}
	}
}
