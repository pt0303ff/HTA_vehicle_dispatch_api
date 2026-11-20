using vehicle_dispatch_app_api.DTOs;

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
}