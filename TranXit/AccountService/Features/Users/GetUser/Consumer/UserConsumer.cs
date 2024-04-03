using AccountService.Database;
using MassTransit;
using SharedServicesManager.Contracts.AuthorizedUser;

namespace AccountService.Features.Users.GetUser.Consumer
{
	public class UserConsumer(AccountDbContext authDbContext) : IConsumer<CheckUser>
	{
		public async Task Consume(ConsumeContext<CheckUser> context)
		{
			var userResponse = await authDbContext.Users
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
