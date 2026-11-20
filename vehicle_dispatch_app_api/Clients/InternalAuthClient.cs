
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


        // ==========================================
        // 共用：建立內網 API POST Request
        // ==========================================

        private async Task<HttpResponseMessage> SendPostAsync<T>(
            string endpoint,
            T request,
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
                endpoint);

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


        // ==========================================
        // 1. 首次開通帳號
        // POST: api/internal/auth/activate
        // ==========================================

        public Task<HttpResponseMessage> ActivateAsync(
            ActivateDriverRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendPostAsync(
                "api/internal/auth/activate",
                request,
                cancellationToken);
        }


        // ==========================================
        // 2. 司機登入
        // POST: api/internal/auth/login
        // ==========================================

        public Task<HttpResponseMessage> LoginAsync(
            DriverLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendPostAsync(
                "api/internal/auth/login",
                request,
                cancellationToken);
        }


        // ==========================================
        // 3. Refresh Token 更新
        // POST: api/internal/auth/refresh
        // ==========================================

        public Task<HttpResponseMessage> RefreshAsync(
            DriverRefreshRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendPostAsync(
                "api/internal/auth/refresh",
                request,
                cancellationToken);
        }


        // ==========================================
        // 4. 司機登出
        // POST: api/internal/auth/logout
        // ==========================================

        public Task<HttpResponseMessage> LogoutAsync(
            DriverLogoutRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendPostAsync(
                "api/internal/auth/logout",
                request,
                cancellationToken);
        }
    }
}
