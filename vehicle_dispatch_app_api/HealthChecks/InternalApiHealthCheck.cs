
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace vehicle_dispatch_app_api.HealthChecks
{
    public class InternalApiHealthCheck : IHealthCheck
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public InternalApiHealthCheck(
            IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var client = _httpClientFactory
                .CreateClient("InternalApiHealth");

            try
            {
                using var response = await client.GetAsync(
                    "api/health/ready",
                    cancellationToken
                );

                if (response.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Healthy(
                        "Internal API is ready."
                    );
                }

                return HealthCheckResult.Unhealthy(
                    $"Internal API returned HTTP {(int)response.StatusCode}."
                );
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Unable to connect to Internal API.",
                    ex
                );
            }
        }
    }
}
