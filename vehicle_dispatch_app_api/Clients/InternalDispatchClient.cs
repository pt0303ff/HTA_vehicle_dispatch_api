using vehicle_dispatch_app_api.DTOs;
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


    // ==================================================
    // GET：取得司機任務
    // ==================================================

    public async Task<HttpResponseMessage> GetTasksAsync(
        Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var apiKey =
            _configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "找不到 InternalApi:ApiKey 設定");
        }


        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "api/internal/dispatch/tasks");


        request.Headers.Add(
            "X-Internal-Api-Key",
            apiKey);

        request.Headers.Add(
            "X-Driver-Id",
            driverId.ToString());


        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    // ==================================================
    // GET：取得單筆任務詳細資訊
    // ==================================================

    public async Task<HttpResponseMessage> GetTaskDetailAsync(
        string dispatchNo,
        Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var apiKey =
            _configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "找不到 InternalApi:ApiKey 設定");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/internal/dispatch/tasks/{Uri.EscapeDataString(dispatchNo)}"
        );

        request.Headers.Add(
            "X-Internal-Api-Key",
            apiKey);

        request.Headers.Add(
            "X-Driver-Id",
            driverId.ToString());

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    // ==================================================
    // POST：同步任務執行資料至內網 API
    // ==================================================

    public async Task<HttpResponseMessage> SyncExecutionAsync(
        JsonElement payload,
        string authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        // 1. 取得內部服務驗證 Key
        var apiKey =
            _configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "找不到 InternalApi:ApiKey 設定");
        }

        // 2. 確認傳入資料是 JSON Object
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "同步資料必須是 JSON Object");
        }

        // 3. 確認 Bearer Token
        if (!AuthenticationHeaderValue.TryParse(
                authorizationHeader,
                out var authorization) ||
            !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            throw new ArgumentException(
                "Authorization Bearer Token 格式錯誤");
        }

        // 4. 建立 POST Request
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/internal/dispatch/sync");

        // 5. 加入內部服務驗證 Key
        request.Headers.Add(
            "X-Internal-Api-Key",
            apiKey);

        // 6. 轉送 APP 原始 JWT
        request.Headers.Authorization = authorization;

        // 7. 將完整 JSON 資料放入 Body
        request.Content = JsonContent.Create(payload);

        // 8. 發送至內網 API
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

}