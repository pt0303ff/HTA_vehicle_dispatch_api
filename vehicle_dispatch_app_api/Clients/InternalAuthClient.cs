
using System.Net.Http.Headers;
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
            string? dpopProof = null,
            string? authorizationHeader = null,
            string? originalMethod = null,
            string? originalUri = null)
        {
            // ==========================================
            // 1. 取得 Internal API Key
            // ==========================================

            var apiKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "找不到 InternalApi:ApiKey 設定");
            }

            // ==========================================
            // 2. 建立 HTTP Request
            // ==========================================

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                endpoint);

            httpRequest.Headers.Add(
                "X-Internal-Api-Key",
                apiKey);

            // ==========================================
            // 3. 轉送 Authorization: Bearer JWT
            // ==========================================

            if (!string.IsNullOrWhiteSpace(authorizationHeader))
            {
                if (!AuthenticationHeaderValue.TryParse(
                        authorizationHeader,
                        out var authorization) ||
                    !string.Equals(
                        authorization.Scheme,
                        "Bearer",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(
                        authorization.Parameter))
                {
                    throw new ArgumentException(
                        "Authorization Header 格式錯誤",
                        nameof(authorizationHeader));
                }

                httpRequest.Headers.Authorization =
                    authorization;
            }

            // ==========================================
            // 4. 轉送 DPoP Proof
            // 不重新簽章，保留 APP 原始 Proof
            // ==========================================

            if (!string.IsNullOrWhiteSpace(dpopProof))
            {
                httpRequest.Headers.TryAddWithoutValidation(
                    "DPoP",
                    dpopProof);
            }

            // ==========================================
            // 5. 轉送原始 HTTP Method
            // ==========================================

            if (!string.IsNullOrWhiteSpace(originalMethod))
            {
                if (!string.Equals(
                        originalMethod,
                        HttpMethod.Post.Method,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "原始 HTTP Method 不正確",
                        nameof(originalMethod));
                }

                httpRequest.Headers.TryAddWithoutValidation(
                    "X-Original-Method",
                    originalMethod.ToUpperInvariant());
            }

            // ==========================================
            // 6. 轉送 APP 原始公開 URI
            // ==========================================

            if (!string.IsNullOrWhiteSpace(originalUri))
            {
                if (!Uri.TryCreate(
                        originalUri,
                        UriKind.Absolute,
                        out var uri) ||
                    uri.Query.Length > 0 ||
                    uri.Fragment.Length > 0 ||
                    string.IsNullOrWhiteSpace(uri.Host) ||
                    (uri.Scheme != Uri.UriSchemeHttps &&
                     !(_configuration["ASPNETCORE_ENVIRONMENT"]
                           == "Development" &&
                       uri.Scheme == Uri.UriSchemeHttp)))
                {
                    throw new ArgumentException(
                        "原始公開 URI 格式錯誤",
                        nameof(originalUri));
                }

                httpRequest.Headers.TryAddWithoutValidation(
                    "X-Original-Uri",
                    originalUri);
            }

            // ==========================================
            // 7. JSON Request Body
            // ==========================================

            httpRequest.Content = JsonContent.Create(request);

            // ==========================================
            // 8. 發送 Request
            // ==========================================

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
                "api/internal/auth/refresh",
                request,
                cancellationToken,
                dpopProof);
        }

        // ==========================================
        // 5. 司機登出
        // Authorization + DPoP + Original URI
        // ==========================================

        public Task<HttpResponseMessage> LogoutAsync(
            DriverLogoutRequest request,
            string authorizationHeader,
            string dpopProof,
            string originalUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                throw new ArgumentException(
                    "Authorization Header 不可為空",
                    nameof(authorizationHeader));
            }

            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                throw new ArgumentException(
                    "DPoP Proof 不可為空",
                    nameof(dpopProof));
            }

            if (string.IsNullOrWhiteSpace(originalUri))
            {
                throw new ArgumentException(
                    "Original URI 不可為空",
                    nameof(originalUri));
            }

            return SendPostAsync(
                "api/internal/auth/logout",
                request,
                cancellationToken,
                dpopProof,
                authorizationHeader,
                "POST",
                originalUri);
        }
    }
}
