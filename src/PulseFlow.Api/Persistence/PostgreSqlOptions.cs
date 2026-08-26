using Microsoft.Extensions.Options;

namespace PulseFlow.Api.Persistence;

public sealed class PostgreSqlOptions
{
    public const string SectionName = "PostgreSql";

    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int ConnectionTimeoutSeconds => GetWholeSeconds(ConnectionTimeout, nameof(ConnectionTimeout));

    public int CommandTimeoutSeconds => GetWholeSeconds(CommandTimeout, nameof(CommandTimeout));

    private static int GetWholeSeconds(TimeSpan timeout, string propertyName)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalSeconds > int.MaxValue)
        {
            throw new OptionsValidationException(
                SectionName,
                typeof(PostgreSqlOptions),
                [$"{SectionName}:{propertyName} must be greater than zero and no more than {int.MaxValue} seconds."]
            );
        }

        return checked((int)Math.Ceiling(timeout.TotalSeconds));
    }
}
