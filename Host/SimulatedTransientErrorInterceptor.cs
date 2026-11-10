using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Host;

public class SimulatedTransientErrorInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SimulatedTransientErrorInterceptor> logger;
    private readonly int ratePercent;

    public SimulatedTransientErrorInterceptor(
        ILogger<SimulatedTransientErrorInterceptor> logger,
        IConfiguration configuration)
    {
        this.ratePercent = configuration.CommandRatePercent();
        this.logger = logger;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = new())
    {
        var roll = Random.Shared.Next(100);
        if (roll >= this.ratePercent)
        {
            // Nothing happens - just pass through.
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        this.logger.LogDebug("Simulating transient error.");
        throw new PostgresException(
            "Simulated database failover.",
            "FATAL",
            "FATAL",
            "57P01");
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = new())
    {
        var roll = Random.Shared.Next(100);
        if (roll >= this.ratePercent)
        {
            // Nothing happens - just pass through.
            return base.NonQueryExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        this.logger.LogDebug("Simulating transient error (non-query).");
        throw new PostgresException(
            "Simulated database failover.",
            "FATAL",
            "FATAL",
            "57P01");
    }
}
