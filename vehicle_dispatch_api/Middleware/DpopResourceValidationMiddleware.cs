
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.Models.Auth;
using vehicle_dispatch_api.Services.Auth;

namespace vehicle_dispatch_api.Middleware;

public sealed class DpopResourceValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DpopResourceValidationMiddleware> _logger;

    public DpopResourceValidationMiddleware(
        RequestDelegate next,
        ILogger<DpopResourceValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext db,
        DpopProofValidationService dpopService,
        IConfiguration configuration)
    {
        var endpoint = context.GetEndpoint();

        // 沒有 Endpoint 時不處理
        if (endpoint == null)
        {
            await _next(context);
            return;
        }

        // 只處理有 Authorize 的 API
        var authorizeData =
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        var allowAnonymous =
            endpoint.Metadata.GetMetadata<IAllowAnonymous>();

        if (authorizeData.Count == 0 || allowAnonymous != null)
        {
            await _next(context);
            return;
        }

        // ==========================================
        // 1. 驗證內部 API Key
        // ==========================================

        var expectedApiKey =
            configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            await RejectAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "內部服務尚未完成設定");

            return;
        }

        if (!context.Request.Headers.TryGetValue(
                "X-Internal-Api-Key",
                out var suppliedApiKey))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "缺少內部服務驗證資訊");

            return;
        }

        var expectedBytes =
            Encoding.UTF8.GetBytes(expectedApiKey);

        var suppliedBytes =
            Encoding.UTF8.GetBytes(suppliedApiKey.ToString());

        bool apiKeyValid =
            expectedBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                suppliedBytes);

        if (!apiKeyValid)
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "內部服務驗證失敗");

            return;
        }

        // ==========================================
        // 2. 驗證 JWT 身分
        // ==========================================

        var user = context.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "JWT 驗證失敗");

            return;
        }

        var driverIdValue =
            user.FindFirst("sub")?.Value;

        var phoneIdValue =
            user.FindFirst("phone_id")?.Value;

        var tokenType =
            user.FindFirst("token_type")?.Value;

        if (tokenType != "access" ||
            !Guid.TryParse(driverIdValue, out var driverId) ||
            !Guid.TryParse(phoneIdValue, out var phoneId) ||
            driverId == Guid.Empty ||
            phoneId == Guid.Empty)
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "JWT 身分資訊無效");

            return;
        }

        // ==========================================
        // 3. 取得 JWT 的 cnf.jkt
        // ==========================================

        var cnfValue =
            user.FindFirst("cnf")?.Value;

        string expectedJkt;

        try
        {
            if (string.IsNullOrWhiteSpace(cnfValue))
            {
                throw new InvalidOperationException();
            }

            using var cnfDocument =
                JsonDocument.Parse(cnfValue);

            var root = cnfDocument.RootElement;

            if (!root.TryGetProperty(
                    "jkt",
                    out var jktElement) ||
                jktElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException();
            }

            expectedJkt =
                jktElement.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedJkt))
            {
                throw new InvalidOperationException();
            }
        }
        catch
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "JWT 缺少有效的 DPoP 公鑰綁定資訊");

            return;
        }

        // ==========================================
        // 4. 取得 Authorization Bearer Token
        // ==========================================

        if (!context.Request.Headers.TryGetValue(
                "Authorization",
                out var authorizationValues) ||
            !AuthenticationHeaderValue.TryParse(
                authorizationValues.ToString(),
                out var authorization) ||
            !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "缺少有效的 Access Token");

            return;
        }

        var accessToken = authorization.Parameter;

        // ==========================================
        // 5. 取得 DPoP Proof
        // ==========================================

        if (!context.Request.Headers.TryGetValue(
                "DPoP",
                out var dpopValues) ||
            string.IsNullOrWhiteSpace(dpopValues.ToString()))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "缺少 DPoP Proof");

            return;
        }

        var dpopProof = dpopValues.ToString();

        // ==========================================
        // 6. 取得外網 API 原始 Method / URI
        // ==========================================

        if (!context.Request.Headers.TryGetValue(
                "X-Original-Method",
                out var originalMethodValues) ||
            !context.Request.Headers.TryGetValue(
                "X-Original-Uri",
                out var originalUriValues))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "缺少原始 API 請求資訊");

            return;
        }

        var originalMethod =
            originalMethodValues.ToString();

        var originalUri =
            originalUriValues.ToString();

        if (!string.Equals(
                originalMethod,
                context.Request.Method,
                StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "原始 HTTP Method 不一致");

            return;
        }

        var isDevelopment =
            context.RequestServices
                .GetRequiredService<IWebHostEnvironment>()
                .IsDevelopment();

        if (!Uri.TryCreate(
                originalUri,
                UriKind.Absolute,
                out var uri))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "原始 URI 格式錯誤");

            return;
        }

        if ((uri.Scheme != Uri.UriSchemeHttps &&
             !(isDevelopment &&
               uri.Scheme == Uri.UriSchemeHttp)) ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0 ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "原始 URI 不符合 DPoP 規範");

            return;
        }

        // ==========================================
        // 7. 查詢資料庫綁定設備公鑰
        // ==========================================

        var phone = await db.DriverAppPhones
            .AsNoTracking()
            .Where(x =>
                x.PhoneId == phoneId &&
                x.DriverId == driverId &&
                x.Status == DriverAppPhone.StatusBound &&
                x.RevokedAt == null)
            .Select(x => new
            {
                x.PublicKey
            })
            .FirstOrDefaultAsync(
                context.RequestAborted);

        if (phone == null ||
            string.IsNullOrWhiteSpace(phone.PublicKey))
        {
            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "設備不存在、已停用或公鑰無效");

            return;
        }

        // ==========================================
        // 8. 完整驗證 DPoP Proof
        // ==========================================

        var isValid =
            dpopService.ValidateAccessTokenProof(
                dpopProof,
                phone.PublicKey,
                phoneId,
                originalMethod,
                originalUri,
                accessToken,
                expectedJkt,
                out var thumbprint,
                out var error);

        if (!isValid)
        {
            _logger.LogWarning(
                "DPoP 資源 API 驗證失敗，DriverId={DriverId}, PhoneId={PhoneId}, Method={Method}, Path={Path}, Error={Error}",
                driverId,
                phoneId,
                originalMethod,
                context.Request.Path,
                error);

            await RejectAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "DPoP 驗證失敗");

            return;
        }

        // ==========================================
        // 9. 驗證成功
        // ==========================================

        _logger.LogDebug(
            "DPoP 資源 API 驗證成功，DriverId={DriverId}, PhoneId={PhoneId}, Jkt={Jkt}",
            driverId,
            phoneId,
            thumbprint);
        context.Items["DpopValidated"] = true;
        await _next(context);
    }

    // ==========================================
    // 統一拒絕請求
    // ==========================================

    private static async Task RejectAsync(
        HttpContext context,
        int statusCode,
        string message)
    {
        context.Response.StatusCode = statusCode;

        context.Response.ContentType =
            "application/json; charset=utf-8";

        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsJsonAsync(
            new
            {
                success = false,
                message
            },
            cancellationToken: context.RequestAborted);
    }
}
