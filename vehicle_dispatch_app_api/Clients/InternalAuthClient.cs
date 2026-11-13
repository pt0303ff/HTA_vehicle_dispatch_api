
using System.Net.Http.Json;
using vehicle_dispatch_app_api.DTOs.Auth;

namespace vehicle_dispatch_app_api.Clients
{
    public class InternalAuthClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public InternalAuthClient(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // 呼叫內網 API，執行司機帳號開通
        public async Task<HttpResponseMessage> ActivateAsync(
            ActivateDriverRequest request,
            CancellationToken cancellationToken = default)
        {
            // 取得外網 API 自己持有的服務驗證 Key
            var apiKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "找不到 InternalApi:ApiKey 設定");
            }

            // 建立 HTTP Request
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "api/internal/auth/activate");

            // 加入內部服務驗證 Header
            httpRequest.Headers.Add(
                "X-Internal-Api-Key",
                apiKey);

            // 將 DTO 自動轉換成 JSON
            httpRequest.Content = JsonContent.Create(request);

            // 發送至內網 API
            return await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
    }
}
