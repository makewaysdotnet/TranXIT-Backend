using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;

namespace TranXit.IntegrationTests.Infrastructure;

internal static class MassTransitTestTeardown
{
	private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

	public static void StopBus(IServiceProvider services)
	{
		try
		{
			services.GetService<IBusControl>()?.StopAsync(StopTimeout).GetAwaiter().GetResult();
		}
		catch (Exception exception) when (IsBenignTeardownRace(exception))
		{
		}
	}

	public static async ValueTask StopBusAsync(IServiceProvider services)
	{
		try
		{
			var busControl = services.GetService<IBusControl>();
			if (busControl is not null)
			{
				await busControl.StopAsync(StopTimeout);
			}
		}
		catch (Exception exception) when (IsBenignTeardownRace(exception))
		{
		}
	}

	public static bool IsBenignTeardownRace(Exception exception)
	{
		return exception is OperationCanceledException
			|| exception is ObjectDisposedException
			|| exception is ChannelClosedException
			|| (exception is AggregateException aggregateException
				&& aggregateException.Flatten().InnerExceptions.All(IsBenignTeardownRace));
	}
}
