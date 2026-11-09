using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Host;

public class FooHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = new())
    {
        return Task.FromResult(
            HealthCheckResult.Healthy());
    }
}