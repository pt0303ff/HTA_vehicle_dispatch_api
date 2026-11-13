
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using vehicle_dispatch_app_api.Clients;
using vehicle_dispatch_app_api.HealthChecks;
using vehicle_dispatch_app_api.Clients;

var builder = WebApplication.CreateBuilder(args);

// 1. 註冊 Controller
builder.Services.AddControllers();

// 2. 註冊 OpenAPI
builder.Services.AddOpenApi();

// 3. 取得內網 API 網址
var internalApiBaseUrl =
    builder.Configuration["InternalApi:BaseUrl"]
    ?? throw new InvalidOperationException(
        "找不到 InternalApi:BaseUrl 設定");

// 4. 註冊原本的 InternalApiClient
builder.Services.AddHttpClient<InternalApiClient>(client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// 5. 註冊 Health Check 專用 HttpClient
builder.Services.AddHttpClient("InternalApiHealth", client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// ======================================
// Phase 5-2：內網帳號驗證服務
// ======================================

builder.Services.AddHttpClient<InternalAuthClient>(client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// 6. 註冊外網 API 健康檢查
builder.Services
    .AddHealthChecks()
    .AddCheck<InternalApiHealthCheck>(
        "internal_api",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" }
    );

var app = builder.Build();

// 7. 開發環境啟用 Swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Vehicle Dispatch Public API v1");

        options.RoutePrefix = "swagger";
    });
}

// 8. HTTPS 轉址
app.UseHttpsRedirection();

// 9. Controller 路由
app.MapControllers();

// 10. Liveness：只確認外網 API 本身運作
app.MapHealthChecks(
    "/api/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });

// 11. Readiness：確認內網 API 是否正常
app.MapHealthChecks(
    "/api/health/ready",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

// 12. 保留原本 Health Check 路徑，作為 Readiness 別名
app.MapHealthChecks(
    "/api/health",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

app.Run();
