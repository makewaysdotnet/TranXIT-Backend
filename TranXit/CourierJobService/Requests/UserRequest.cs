using MassTransit;
using SharedServicesManager.Contracts.User;
using SharedServicesManager.Helpers;
using System.Security.Claims;

namespace CourierJobService.Requests
{
	public static class UserRequest
	{
		public static async Task<CheckUserResult?> GetCurrentUserAsync(IHttpContextAccessor httpContext, IBus messageBus)
		{
			var userId = HttpContextUser.GetCurrentUserId(httpContext);
			var response = await messageBus.Request<CheckUser, CheckUserResult>(new CheckUser
			{
				UserId = (int)userId!
			});
			if (response.Message.UserId != (int)userId)
			{
				return null;
			}
			return response.Message;
		}
		public static async Task<CheckUserResult?> GetUserAsync(int userId, IBus messageBus)
		{
			var response = await messageBus.Request<CheckUser, CheckUserResult>(new CheckUser
			{
				UserId = userId,
			});
			return response.Message;
		}

	}
}
