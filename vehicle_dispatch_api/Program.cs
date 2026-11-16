using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using vehicle_dispatch_api.Models.Auth;
using vehicle_dispatch_api.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;



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

// 司機登入驗證服務
builder.Services.AddScoped<DriverLoginService>();

// 司機 Refresh Token 更新服務
builder.Services.AddScoped<DriverRefreshService>();

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

// ======================================
// Phase 5-2：JWT 與 Refresh Token
// ======================================

// JWT Access Token 簽發
builder.Services.AddScoped<JwtTokenService>();

// Refresh Token 產生及 Hash
builder.Services.AddSingleton<RefreshTokenService>();

// 原本的 DriverLoginService 註冊保留


// ==========================================
// JWT Authentication 設定
// ==========================================

// 1. 取得 JWT 設定

var jwtIssuer =
    builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "找不到 Jwt:Issuer");

var jwtAudience =
    builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "找不到 Jwt:Audience");

var jwtSigningKey =
    builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException(
        "找不到 Jwt:SigningKey");


// 2. 解碼 Base64 金鑰

byte[] jwtKeyBytes;

try
{
    jwtKeyBytes = Convert.FromBase64String(jwtSigningKey);
}
catch (FormatException ex)
{
    throw new InvalidOperationException(
        "JWT SigningKey 格式錯誤",
        ex);
}

if (jwtKeyBytes.Length < 32)
{
    throw new InvalidOperationException(
        "JWT SigningKey 至少需要 32 Bytes");
}

var jwtSecurityKey =
    new SymmetricSecurityKey(jwtKeyBytes);


// ==========================================
// 3. 註冊 JWT Bearer Authentication
// ==========================================

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 保留原始 Claim 名稱，例如 sub、session_id
        options.MapInboundClaims = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                // 確認簽發者
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                // 確認接收對象
                ValidateAudience = true,
                ValidAudience = jwtAudience,

                // 確認 Token 有效期限
                ValidateLifetime = true,
                RequireExpirationTime = true,

                // 確認簽章
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwtSecurityKey,

                // 允許 30 秒時鐘誤差
                ClockSkew = TimeSpan.FromSeconds(30),

                // 使用 sub 作為使用者識別 Claim
                NameClaimType = "sub"
            };
    });


// ==========================================
// 4. 註冊 Authorization
// ==========================================

builder.Services.AddAuthorization();

var app = builder.Build();

// 6. 啟用 HTTPS 轉址
app.UseHttpsRedirection();

// JWT 身分驗證
app.UseAuthentication();

// API 權限授權
app.UseAuthorization();

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
