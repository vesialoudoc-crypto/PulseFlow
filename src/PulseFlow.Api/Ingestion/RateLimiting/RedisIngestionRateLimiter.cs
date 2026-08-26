using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisIngestionRateLimiter : IIngestionRateLimiter
{
    private const string GlobalKey = "pulseflow:rate-limit:ingestion:global";
    private static readonly string FixedWindowScript = LoadScript();

    private readonly IDatabase _database;
    private readonly IngestionRateLimitOptions _options;
    private readonly ILogger<RedisIngestionRateLimiter> _logger;
    private readonly RedisOptions _redisOptions;

    public RedisIngestionRateLimiter(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<IngestionRateLimitOptions> options,
        IOptions<RedisOptions> redisOptions,
        ILogger<RedisIngestionRateLimiter> logger
    )
    {
        _database = connectionMultiplexer.GetDatabase();
        _options = options.Value;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    public async Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // Execute the whole fixed-window check atomically inside Redis.
            // keys[0]   -> Lua KEYS[1] = GlobalKey
            // values[0] -> Lua ARGV[1] = RequestLimit
            // values[1] -> Lua ARGV[2] = WindowDuration in milliseconds
            var result = await _database
                .ScriptEvaluateAsync(
                    script: FixedWindowScript,
                    keys: [GlobalKey],
                    values: [_options.RequestLimit, GetWindowDurationMilliseconds()]
                )
                .WaitAsync(ct);

            // Lua returns two values:
            // { 1, ttl } -> allowed
            // { 0, ttl } -> limit exceeded
            long[]? values = (long[]?)result;

            // Unexpected Redis response means the limiter cannot make a safe decision.
            if (values is null || values.Length != 2)
            {
                return new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable);
            }

            bool isAllowed = values[0] == 1;

            // Redis PTTL may return a negative value in special cases.
            // RetryAfter should never be negative.
            if (isAllowed)
            {
                return new IngestionRateLimitResult(IngestionRateLimitStatus.Allowed);
            }

            long remainingTtlMilliseconds = Math.Max(0, values[1]);
            return new IngestionRateLimitResult(
                IngestionRateLimitStatus.Exceeded,
                TimeSpan.FromMilliseconds(remainingTtlMilliseconds)
            );
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Redis rate-limit operation timed out after {ConfiguredTimeout}.",
                _redisOptions.AsyncTimeout
            );
            return new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable);
        }
        catch (RedisException exception)
        {
            _logger.LogError(exception, "Redis rate limiter is unavailable.");
            return new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable);
        }
        catch (ObjectDisposedException exception)
        {
            _logger.LogError(exception, "Redis connection was disposed.");
            return new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable);
        }
    }

    private long GetWindowDurationMilliseconds()
    {
        double milliseconds = _options.WindowDuration.TotalMilliseconds;
        return (long)Math.Ceiling(milliseconds);
    }

    private static string LoadScript()
    {
        var assembly = typeof(RedisIngestionRateLimiter).Assembly;

        // The Lua script is included as an EmbeddedResource in PulseFlow.Api.csproj.
        using var stream = assembly.GetManifestResourceStream(
            typeof(RedisIngestionRateLimiter),
            "Scripts.FixedWindowRateLimit.lua"
        );

        if (stream is null)
        {
            throw new InvalidOperationException("Rate limit Lua script was not found.");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
