using Microsoft.Extensions.Options;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan AsyncTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public int ConnectTimeoutMilliseconds => GetWholeMilliseconds(ConnectTimeout, nameof(ConnectTimeout));

    public int AsyncTimeoutMilliseconds => GetWholeMilliseconds(AsyncTimeout, nameof(AsyncTimeout));

    private static int GetWholeMilliseconds(TimeSpan timeout, string propertyName)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new OptionsValidationException(
                SectionName,
                typeof(RedisOptions),
                [
                    $"{SectionName}:{propertyName} must be greater than zero and no more than {int.MaxValue} milliseconds.",
                ]
            );
        }

        return checked((int)Math.Ceiling(timeout.TotalMilliseconds));
    }
}
