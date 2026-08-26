using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisConnectionMultiplexerFactory
{
    private readonly Func<Task<IConnectionMultiplexer>> _connectAsync;

    public RedisConnectionMultiplexerFactory(ConfigurationOptions configurationOptions)
    {
        ArgumentNullException.ThrowIfNull(configurationOptions);
        _connectAsync = async () => await ConnectionMultiplexer.ConnectAsync(configurationOptions);
    }

    internal RedisConnectionMultiplexerFactory(Func<Task<IConnectionMultiplexer>> connectAsync)
    {
        _connectAsync = connectAsync;
    }

    public Task<IConnectionMultiplexer> ConnectAsync()
    {
        return _connectAsync();
    }
}
