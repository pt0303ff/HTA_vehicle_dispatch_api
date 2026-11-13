using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using vehicle_dispatch_api.Models.Auth;
using vehicle_dispatch_api.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

// 1. 註冊 API Controller
builder.Services.AddControllers();

// 註冊 Health Check，並加入 SQL Server 連線檢查
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "sql_server",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" }
    );

// 3. 註冊 OpenAPI
builder.Services.AddOpenApi();

// ======================================
// Phase 5-2：司機帳號與密碼服務
// ======================================

// PasswordHasher 統一規格：
// CompatibilityMode = IdentityV3
// PBKDF2-HMAC-SHA512
// IterationCount = 100,000
//
// 必須與管理者主系統 .NET 8 的 PasswordHasherOptions 相同。
// Salt 由 PasswordHasher 每次自動隨機產生。
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode =
        PasswordHasherCompatibilityMode.IdentityV3;

    options.IterationCount = 100_000;
});

// ASP.NET Core Identity PasswordHasher
builder.Services.AddScoped<
    IPasswordHasher<DriverAppAccount>,
    PasswordHasher<DriverAppAccount>>();

// 密碼雜湊與驗證
builder.Services.AddScoped<DriverPasswordService>();

// 暫時保留原本的帳號建立 Service 註冊，避免影響既有相依。
// 目前已確定由管理者主系統直接寫入 vehicledriver，
// 因此 APP 開通流程不應呼叫此 Service 建立司機帳號。
builder.Services.AddScoped<DriverAccountProvisioningService>();

// 司機首次開通帳號服務
builder.Services.AddScoped<DriverActivationService>();

// 4. 取得 SQL Server 連線字串
var connectionString = builder.Configuration
    .GetConnectionString("HTAPortal2024Db")
    ?? throw new InvalidOperationException(
        "找不到 DispatchDb 資料庫連線設定");

// 5. 註冊 ApplicationDbContext
builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

var app = builder.Build();

// 6. 啟用 HTTPS 轉址
app.UseHttpsRedirection();

// 7. 開發環境啟用 Swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Vehicle Dispatch API v1");

        options.RoutePrefix = "swagger";
    });
}

// 8. 啟用 Controller 路由
app.MapControllers();

// 9. API Health Check
// Liveness：只確認 API 程式還能正常回應
app.MapHealthChecks("/api/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });

// Readiness：檢查 SQL Server 是否正常
app.MapHealthChecks("/api/health/ready",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

// 保留原本的 Health Check 網址，作為 Readiness 別名
app.MapHealthChecks("/api/health",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

app.Run();
