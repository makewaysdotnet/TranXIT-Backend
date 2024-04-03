using MassTransit;
using SharedServicesManager.Contracts.AuthorizedUser;
using System.Security.Claims;

namespace JobService.Utils
{
	public static class UserValidation
	{
		public static async Task<CheckUserResult?> IsUserValidAsync(IHttpContextAccessor httpContext, IBus messageBus)
		{
			var userId = httpContext!.HttpContext!.User.FindFirstValue("UserId");
			if (userId is null)
			{
				return null;
			}
			var response = await messageBus.Request<CheckUser, CheckUserResult>(new CheckUser
			{
				UserId = int.Parse(userId)
			});
			if (response.Message.UserId != int.Parse(userId))
			{
				return null;
			}
			return response.Message;
		}
	}
}
