
using System.Net.Http.Json;
using vehicle_dispatch_app_api.DTOs;

namespace vehicle_dispatch_app_api.Clients
{
    public class InternalApiClient
    {
        private readonly HttpClient _httpClient;

        public InternalApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // 呼叫內網 API 的 Ping
        public async Task<InternalPingResponse?> GetPingAsync(
            CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<InternalPingResponse>(
                "api/system/ping",
                cancellationToken
            );
        }
    }
}
