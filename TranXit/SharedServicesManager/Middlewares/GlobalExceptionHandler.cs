using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SharedServicesManager.Middlewares
{
	public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
	{
		public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
			Exception exception,
			CancellationToken cancellationToken)
		{
			var correlationId = httpContext.TraceIdentifier;
			logger.LogError(exception, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

			var problemDetails = new ProblemDetails
			{
				Status = StatusCodes.Status500InternalServerError,
				Title = "Server Error",
				Type = "https://httpstatuses.com/500",
				Detail = "An unexpected error occurred. Provide the correlationId when contacting support."
			};
			problemDetails.Extensions["correlationId"] = correlationId;

			httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
			await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
			return true;
		}
	}
}
