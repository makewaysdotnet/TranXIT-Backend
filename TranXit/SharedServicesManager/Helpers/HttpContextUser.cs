using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace SharedServicesManager.Helpers
{
	public static class HttpContextUser
	{
		public static int GetCurrentUserId(IHttpContextAccessor httpContext)
		=> Convert.ToInt32(httpContext!.HttpContext!.User.FindFirstValue("UserId"));

		public static string GetCurrentUserRole(IHttpContextAccessor httpContext)
		=> httpContext!.HttpContext!.User.FindFirstValue(ClaimTypes.Role)!;
	}
}
