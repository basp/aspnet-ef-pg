using Microsoft.Extensions.Configuration;

namespace Host;

public static class DemoConfiguration
{
    public static int ConnectionRatePercent(
        this IConfiguration configuration)
    {
        return configuration.ReadInt(
            "Demo:FaultInjection:ConnectionRatePercent",
            fallback: 50,
            min: 0,
            max: 100);
    }

    public static int CommandRatePercent(
        this IConfiguration configuration)
    {
        return configuration.ReadInt(
            "Demo:FaultInjection:CommandRatePercent",
            fallback: 50,
            min: 0,
            max: 100);
    }

    public static int RetryMaxRetryCount(
        this IConfiguration configuration)
    {
        return configuration.ReadInt(
            "Demo:Retry:MaxRetryCount",
            fallback: 10,
            min: 0,
            max: int.MaxValue);
    }

    public static int RetryMaxRetryDelaySeconds(
        this IConfiguration configuration)
    {
        return configuration.ReadInt(
            "Demo:Retry:MaxRetryDelaySeconds",
            fallback: 30,
            min: 0,
            max: int.MaxValue);
    }

    private static int ReadInt(
        this IConfiguration configuration,
        string key,
        int fallback,
        int min,
        int max)
    {
        var value = configuration[key];

        if (value is null)
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsed)
            || parsed < min
            || parsed > max)
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be an integer in the range [{min}, {max}]. Actual value: '{value}'.");
        }

        return parsed;
    }
}
