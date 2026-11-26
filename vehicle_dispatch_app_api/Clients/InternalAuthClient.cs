
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
            CancellationToken cancellationToken = default,
            string? dpopProof = null)
        {
            var apiKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "找不到 InternalApi:ApiKey 設定");
            }

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                endpoint);

            httpRequest.Headers.Add(
                "X-Internal-Api-Key",
                apiKey);

            // 轉送 DPoP Proof，不重新簽章
            if (!string.IsNullOrWhiteSpace(dpopProof))
            {
                httpRequest.Headers.Add(
                    "DPoP",
                    dpopProof);
            }

            httpRequest.Content = JsonContent.Create(request);

            return await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }

        // ==========================================
        // 1. 首次開通帳號
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
        // 2. 取得 DPoP Nonce
        // ==========================================

        public Task<HttpResponseMessage> GetNonceAsync(
            DpopNonceRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendPostAsync(
                "api/internal/auth/nonce",
                request,
                cancellationToken);
        }

        // ==========================================
        // 3. 司機登入
        // ==========================================

        public Task<HttpResponseMessage> LoginAsync(
            DriverLoginRequest request,
            string dpopProof,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                throw new ArgumentException(
                    "DPoP Proof 不可為空",
                    nameof(dpopProof));
            }

            return SendPostAsync(
                "api/internal/auth/login",
                request,
                cancellationToken,
                dpopProof);
        }

        // ==========================================
        // 4. Refresh Token 更新
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
        // 5. 司機登出
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
