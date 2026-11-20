
using System;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

using vehicle_dispatch_app_api.Clients;
using vehicle_dispatch_app_api.HealthChecks;

var builder = WebApplication.CreateBuilder(args);


// ==========================================
// 1. Controller 與 OpenAPI
// ==========================================

builder.Services.AddControllers();


builder.Services.AddOpenApi(options =>
{
    // ==========================================
    // 1. 註冊 Bearer Security Scheme
    // ==========================================

    options.AddDocumentTransformer(
        (document, context, cancellationToken) =>
        {
            document.Components ??= new OpenApiComponents();

            document.Components.SecuritySchemes =
                new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,

                        Scheme = "bearer",

                        BearerFormat = "JWT",

                        Description = "請輸入登入取得的 Access Token"
                    }
                };

            return Task.CompletedTask;
        });


    // ==========================================
    // 2. 只替需要登入的 API 加上 Bearer
    // ==========================================

    options.AddOperationTransformer(
        (operation, context, cancellationToken) =>
        {
            var metadata =
                context.Description.ActionDescriptor.EndpointMetadata;

            // 是否有 [Authorize]
            var hasAuthorize =
                metadata.OfType<IAuthorizeData>().Any();

            // 是否有 [AllowAnonymous]
            var hasAllowAnonymous =
                metadata.OfType<IAllowAnonymous>().Any();

            // 不需要登入的 API 不加入 JWT 要求
            if (!hasAuthorize || hasAllowAnonymous)
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];

            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecuritySchemeReference(
                            "Bearer",
                            context.Document)
                    ] = []
                });

            return Task.CompletedTask;
        });
});



// ==========================================
// 2. 取得內網 API 網址
// ==========================================

var internalApiBaseUrl =
    builder.Configuration["InternalApi:BaseUrl"]
    ?? throw new InvalidOperationException(
        "找不到 InternalApi:BaseUrl 設定");


// ==========================================
// 3. 註冊 InternalApiClient
// ==========================================

builder.Services.AddHttpClient<InternalApiClient>(client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ==========================================
// 派車任務 API Client
// ==========================================

builder.Services.AddHttpClient<InternalDispatchClient>(client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});


// ==========================================
// 4. 註冊 Health Check HttpClient
// ==========================================

builder.Services.AddHttpClient("InternalApiHealth", client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});


// ==========================================
// 5. 註冊內網帳號驗證服務
// ==========================================

builder.Services.AddHttpClient<InternalAuthClient>(client =>
{
    client.BaseAddress = new Uri(internalApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});


// ==========================================
// 6. 註冊 Health Check
// ==========================================

builder.Services
    .AddHealthChecks()
    .AddCheck<InternalApiHealthCheck>(
        "internal_api",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" }
    );


// ==========================================
// Phase 5-2：JWT Authentication
// ==========================================

// 取得與內網 API 相同的 JWT 設定

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


// ==========================================
// 7. Base64 解碼 SigningKey
// ==========================================

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
// 8. 註冊 JWT Bearer Authentication
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
                // Issuer
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                // Audience
                ValidateAudience = true,
                ValidAudience = jwtAudience,

                // Token 有效期限
                ValidateLifetime = true,
                RequireExpirationTime = true,

                // JWT 簽章
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwtSecurityKey,

                // 時間誤差
                ClockSkew = TimeSpan.FromSeconds(30),

                // 使用 sub 作為使用者識別
                NameClaimType = "sub"
            };
    });


// ==========================================
// 9. 註冊 Authorization
// ==========================================

builder.Services.AddAuthorization();


var app = builder.Build();


// ==========================================
// 10. HTTPS
// ==========================================

// 開發環境允許 HTTP，方便 Android Emulator 測試
// 正式環境強制使用 HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
// ==========================================
// 11. JWT Authentication / Authorization
// ==========================================

// 必須先驗證身分
app.UseAuthentication();

// 再檢查 API 存取權限
app.UseAuthorization();


// ==========================================
// 12. Swagger
// ==========================================

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


// ==========================================
// 13. Controller
// ==========================================

app.MapControllers();


// ==========================================
// 14. Liveness
// ==========================================

app.MapHealthChecks(
    "/api/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });


// ==========================================
// 15. Readiness
// ==========================================

app.MapHealthChecks(
    "/api/health/ready",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });


// ==========================================
// 16. 保留原本 Health Check 路徑
// ==========================================

app.MapHealthChecks(
    "/api/health",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });


app.Run();
