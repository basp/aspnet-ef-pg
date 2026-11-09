using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Host;

public class SimulatedTransientErrorInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SimulatedTransientErrorInterceptor> logger;

    public SimulatedTransientErrorInterceptor(
        ILogger<SimulatedTransientErrorInterceptor> logger)
    {
        this.logger = logger;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, 
        CommandEventData eventData, 
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = new())
    {
        var roll = Random.Shared.Next(100);
        if (roll < 30)
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
}