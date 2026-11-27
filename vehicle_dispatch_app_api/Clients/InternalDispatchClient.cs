
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace vehicle_dispatch_app_api.Clients;

public class InternalDispatchClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public InternalDispatchClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // ==========================================
    // 共用：建立內網 API Request
    // ==========================================

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string authorizationHeader,
        string dpopProof,
        string originalUri,
        Guid? driverId = null)
    {
        var apiKey = _configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "找不到 InternalApi:ApiKey 設定");

        if (!AuthenticationHeaderValue.TryParse(
                authorizationHeader,
                out var authorization) ||
            !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            throw new ArgumentException("JWT 格式錯誤");
        }

        if (string.IsNullOrWhiteSpace(dpopProof))
            throw new ArgumentException("DPoP Proof 不可為空");

        if (!Uri.TryCreate(
                originalUri,
                UriKind.Absolute,
                out var uri) ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            throw new ArgumentException("原始公開 URI 格式錯誤");
        }

        var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization = authorization;

        request.Headers.Add("X-Internal-Api-Key", apiKey);
        request.Headers.Add("DPoP", dpopProof);
        request.Headers.Add("X-Original-Method", method.Method);
        request.Headers.Add("X-Original-Uri", originalUri);

        if (driverId.HasValue)
        {
            request.Headers.Add(
                "X-Driver-Id",
                driverId.Value.ToString());
        }

        return request;
    }

    // ==========================================
    // GET：取得司機任務
    // ==========================================

    public async Task<HttpResponseMessage> GetTasksAsync(
        Guid driverId,
        string authorizationHeader,
        string dpopProof,
        string originalUri,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "api/internal/dispatch/tasks",
            authorizationHeader,
            dpopProof,
            originalUri,
            driverId);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    // ==========================================
    // GET：取得任務詳細資訊
    // ==========================================

    public async Task<HttpResponseMessage> GetTaskDetailAsync(
        string dispatchNo,
        Guid driverId,
        string authorizationHeader,
        string dpopProof,
        string originalUri,
        CancellationToken cancellationToken = default)
    {
        var path =
            $"api/internal/dispatch/tasks/{Uri.EscapeDataString(dispatchNo)}";

        using var request = CreateRequest(
            HttpMethod.Get,
            path,
            authorizationHeader,
            dpopProof,
            originalUri,
            driverId);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    // ==========================================
    // POST：同步任務執行資料
    // ==========================================

    public async Task<HttpResponseMessage> SyncExecutionAsync(
        JsonElement payload,
        string authorizationHeader,
        string dpopProof,
        string originalUri,
        CancellationToken cancellationToken = default)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("同步資料必須是 JSON Object");

        using var request = CreateRequest(
            HttpMethod.Post,
            "api/internal/dispatch/sync",
            authorizationHeader,
            dpopProof,
            originalUri);

        request.Content = JsonContent.Create(payload);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }
}
