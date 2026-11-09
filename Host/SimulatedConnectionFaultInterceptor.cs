using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Host;

public class SimulatedConnectionFaultInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<SimulatedConnectionFaultInterceptor> logger;

    public SimulatedConnectionFaultInterceptor(
        ILogger<SimulatedConnectionFaultInterceptor> logger)
    {
        // TODO: Simulate 08001
        
        this.logger = logger;
    }
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, 
        ConnectionEventData eventData, 
        InterceptionResult result,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var roll = Random.Shared.Next(100);
        if (roll < 50)
        {
            // Nothing happens - just pass through.
            return base.ConnectionOpeningAsync(
                connection, 
                eventData, 
                result, 
                cancellationToken);
        }
        
        this.logger.LogDebug("Simulating connection fault.");
        throw new PostgresException(
            "Simulated connection fault.", 
            "FATAL", 
            "FATAL", 
            "08001");
    }
}