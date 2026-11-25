using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.Models.Auth;
using vehicle_dispatch_api.OpenApi;
using vehicle_dispatch_api.Services.Auth;
using vehicle_dispatch_api.Services.Dispatch;



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
// 3. 註冊 OpenAPI
// 加入 JWT Bearer 認證設定，讓 Swagger 可以輸入 Access Token

builder.Services.AddOpenApi(options =>
{
    // 註冊 Bearer Security Scheme
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

    // 只替有 [Authorize] 的 API 加上 JWT 安全需求
    options.AddOperationTransformer(
        (operation, context, cancellationToken) =>
        {
            var requiresAuthorization =
                context.Description.ActionDescriptor.EndpointMetadata
                    .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                    .Any();

            if (requiresAuthorization)
            {
                operation.Security ??= [];

                operation.Security.Add(
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(
                            "Bearer",
                            context.Document)] = []
                    });
            }

            return Task.CompletedTask;
        });
});

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

// JWT Session 即時驗證
builder.Services.AddScoped<JwtSessionValidationService>();

// 司機 Refresh Token 更新服務
builder.Services.AddScoped<DriverRefreshService>();

// ==========================================
// Phase 6-4：離線任務同步服務
// ==========================================

builder.Services.AddScoped<DriverDispatchSyncService>();

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
// Logout 登出服務
// ==========================================

builder.Services.AddScoped<DriverLogoutService>();

builder.Services.AddScoped<DriverDispatchService>();
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
        // 保留原始 Claim 名稱
        options.MapInboundClaims = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateLifetime = true,
                RequireExpirationTime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwtSecurityKey,

                ClockSkew = TimeSpan.FromSeconds(30),

                NameClaimType = "sub"
            };


        // ==========================================
        // JWT 驗證成功後，進一步檢查 DB Session
        // ==========================================

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;

                // 1. 取得 JWT Claims

                var driverIdValue =
                    principal?.FindFirst("sub")?.Value;

                var sessionIdValue =
                    principal?.FindFirst("session_id")?.Value;

                var phoneIdValue =
                    principal?.FindFirst("phone_id")?.Value;

                var tokenType =
                    principal?.FindFirst("token_type")?.Value;


                // 2. 確認 Token 類型及 GUID 格式

                if (tokenType != "access" ||
                    !Guid.TryParse(
                        driverIdValue, out var driverId) ||
                    !Guid.TryParse(
                        sessionIdValue, out var sessionId) ||
                    !Guid.TryParse(
                        phoneIdValue, out var phoneId))
                {
                    context.Fail("Invalid JWT claims.");
                    return;
                }


                // 3. 從 DI 取得 Session 驗證 Service

                var sessionService =
                    context.HttpContext.RequestServices
                        .GetRequiredService<
                            JwtSessionValidationService>();

                var logger =
                    context.HttpContext.RequestServices
                        .GetRequiredService<
                            ILogger<JwtSessionValidationService>>();


                // 4. 查詢資料庫驗證 Session

                try
                {
                    var isValid =
                        await sessionService.IsSessionValidAsync(
                            driverId,
                            sessionId,
                            phoneId,
                            context.HttpContext.RequestAborted
                        );

                    // 5. Session 無效，拒絕 JWT

                    if (!isValid)
                    {
                        context.Fail(
                            "Session is invalid or revoked.");

                        return;
                    }

                    // Session 有效，繼續執行 API
                }
                catch (OperationCanceledException)
                    when (context.HttpContext.RequestAborted
                        .IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // DB 或 Session 驗證服務發生錯誤
                    // 記錄錯誤並停止本次請求，不允許繞過驗證

                    logger.LogError(
                        ex,
                        "JWT Session 驗證時發生資料庫錯誤");

                    throw;
                }
            }
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
