
using System;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Services.Auth;

namespace vehicle_dispatch_api.Controllers
{
    [ApiController]
    [Route("api/internal/auth")]
    public class InternalAuthController : ControllerBase
    {
        private readonly DriverActivationService _activationService;
        private readonly DriverLoginService _loginService;
        private readonly DpopNonceService _dpopNonceService;
        private readonly DriverRefreshService _refreshService;
        private readonly DriverLogoutService _logoutService;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InternalAuthController> _logger;

        public InternalAuthController(
            DriverActivationService activationService,
            DriverLoginService loginService,
            DpopNonceService dpopNonceService,
            DriverRefreshService refreshService,
            DriverLogoutService logoutService,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            ILogger<InternalAuthController> logger)
        {
            _activationService = activationService;
            _loginService = loginService;
            _dpopNonceService = dpopNonceService;
            _refreshService = refreshService;
            _logoutService = logoutService;
            _environment = environment;
            _configuration = configuration;
            _logger = logger;
        }

        // ==========================================
        // 內部 API Key 驗證
        // ==========================================

        private bool IsInternalApiAuthorized(string? suppliedKey)
        {
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey) ||
                string.IsNullOrWhiteSpace(suppliedKey))
            {
                return false;
            }

            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey);

            return expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);
        }

        private IActionResult InternalApiConfigurationError()
        {
            _logger.LogError(
                "尚未設定 InternalApi:ApiKey");

            return StatusCode(500, new
            {
                success = false,
                message = "內部服務尚未完成設定"
            });
        }

        // ==========================================
        // DPoP URL 設定驗證
        // ==========================================

        private bool IsValidDpopUri(string? uri)
        {
            if (!Uri.TryCreate(
                    uri,
                    UriKind.Absolute,
                    out var uriObject))
            {
                return false;
            }

            if (uriObject.Scheme == Uri.UriSchemeHttps)
            {
                return true;
            }

            return _environment.IsDevelopment() &&
                   uriObject.Scheme == Uri.UriSchemeHttp;
        }

        // ==========================================
        // POST: api/internal/auth/activate
        // 司機帳號開通
        // ==========================================

        [HttpPost("activate")]
        public async Task<IActionResult> Activate(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromBody] ActivateDriverRequest request,

            CancellationToken cancellationToken)
        {
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return InternalApiConfigurationError();
            }

            if (!IsInternalApiAuthorized(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            try
            {
                var result =
                    await _activationService.ActivateAsync(
                        request,
                        cancellationToken);

                if (result == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "開通資訊無效或已過期，請確認後重試或聯絡管理員"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "司機帳號開通成功",
                    driverId = result.DriverId,
                    phoneId = result.PhoneId,
                    accountStatus = result.AccountStatus
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機帳號開通時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "帳號開通服務發生錯誤"
                });
            }
        }

        // ==========================================
        // POST: api/internal/auth/nonce
        // 取得 DPoP Nonce
        // ==========================================

        [HttpPost("nonce")]
        public IActionResult GetNonce(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromBody] DpopNonceRequest request)
        {
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return InternalApiConfigurationError();
            }

            if (!IsInternalApiAuthorized(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            if (request == null ||
                request.PhoneId == Guid.Empty)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Phone ID 無效"
                });
            }

            try
            {
                var nonce =
                    _dpopNonceService.CreateNonce(
                        request.PhoneId);

                Response.Headers["DPoP-Nonce"] = nonce;

                return Ok(new
                {
                    success = true,
                    nonce = nonce,
                    expiresIn = 120
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "產生 DPoP Nonce 時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "無法產生驗證 Nonce"
                });
            }
        }

        // ==========================================
        // POST: api/internal/auth/login
        // DPoP + JWT 司機登入
        // ==========================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromHeader(Name = "DPoP")]
            string? dpopProof,

            [FromBody] DriverLoginRequest request,

            CancellationToken cancellationToken)
        {
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return InternalApiConfigurationError();
            }

            if (!IsInternalApiAuthorized(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "缺少 DPoP Proof"
                });
            }

            // 取得 APP 實際呼叫的外網登入 URL
            var expectedUri =
                _configuration["Dpop:LoginUri"];

            if (!IsValidDpopUri(expectedUri))
            {
                _logger.LogError(
                    "Dpop:LoginUri 未設定或不是符合環境要求的 URL");

                return StatusCode(500, new
                {
                    success = false,
                    message = "DPoP LoginUri 設定錯誤"
                });
            }

            try
            {
                var result =
                    await _loginService.LoginAsync(
                        request,
                        dpopProof,
                        expectedUri!,
                        cancellationToken);

                if (result == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "帳號、密碼、設備或 DPoP 驗證失敗"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "登入成功",

                    driverId = result.DriverId,

                    sessionId = result.SessionId,

                    sessionIdleExpiresAt =
                        result.SessionIdleExpiresAt,

                    sessionAbsoluteExpiresAt =
                        result.SessionAbsoluteExpiresAt,

                    accountStatus = result.AccountStatus,

                    tokenType = result.TokenType,

                    accessToken = result.AccessToken,

                    accessTokenExpiresAt =
                        result.AccessTokenExpiresAt,

                    refreshToken = result.RefreshToken,

                    refreshTokenExpiresAt =
                        result.RefreshTokenExpiresAt
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機 DPoP 登入驗證時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "登入服務發生錯誤"
                });
            }
        }

        // ==========================================
        // POST: api/internal/auth/refresh
        // DPoP + Refresh Token Rotation
        // ==========================================

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromHeader(Name = "DPoP")]
            string? dpopProof,

            [FromBody] DriverRefreshRequest request,

            CancellationToken cancellationToken)
        {
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return InternalApiConfigurationError();
            }

            if (!IsInternalApiAuthorized(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // DPoP Proof 必須存在
            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "缺少 DPoP Proof"
                });
            }

            // 取得 APP 實際呼叫的外網 Refresh URL
            var expectedUri =
                _configuration["Dpop:RefreshUri"];

            if (!IsValidDpopUri(expectedUri))
            {
                _logger.LogError(
                    "Dpop:RefreshUri 未設定或不是符合環境要求的 URL");

                return StatusCode(500, new
                {
                    success = false,
                    message = "DPoP RefreshUri 設定錯誤"
                });
            }

            if (request == null ||
                string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Refresh Token 不可為空"
                });
            }

            try
            {
                var result =
                    await _refreshService.RefreshAsync(
                        request.RefreshToken,
                        dpopProof,
                        expectedUri!,
                        cancellationToken);

                if (result == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Refresh Token、設備或 DPoP 驗證失敗，或 Session 已失效"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Token 更新成功",

                    driverId = result.DriverId,

                    sessionId = result.SessionId,

                    sessionIdleExpiresAt =
                        result.SessionIdleExpiresAt,

                    sessionAbsoluteExpiresAt =
                        result.SessionAbsoluteExpiresAt,

                    tokenType = result.TokenType,

                    accessToken = result.AccessToken,

                    accessTokenExpiresAt =
                        result.AccessTokenExpiresAt,

                    refreshToken = result.RefreshToken,

                    refreshTokenExpiresAt =
                        result.RefreshTokenExpiresAt,

                    tokenVersion = result.TokenVersion
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Refresh Token 更新時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "Token 更新服務發生錯誤"
                });
            }
        }

        // ==========================================
        // POST: api/internal/auth/logout
        // 司機登出
        // ==========================================


        // ==========================================
        // POST: api/internal/auth/logout
        // JWT + DPoP 司機登出
        // ==========================================

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(
            [FromHeader(Name = "X-Internal-Api-Key")]
    string? suppliedKey,

            [FromBody] DriverLogoutRequest request,

            CancellationToken cancellationToken)
        {
            // ==========================================
            // 1. 確認 Internal API Key
            // ==========================================

            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return InternalApiConfigurationError();
            }

            if (!IsInternalApiAuthorized(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // ==========================================
            // 2. 確認 JWT 身分
            // ==========================================

            var driverIdValue =
                User.FindFirst("sub")?.Value;

            var sessionIdValue =
                User.FindFirst("session_id")?.Value;

            var phoneIdValue =
                User.FindFirst("phone_id")?.Value;

            var tokenType =
                User.FindFirst("token_type")?.Value;

            if (tokenType != "access" ||
                !Guid.TryParse(driverIdValue, out var driverId) ||
                !Guid.TryParse(sessionIdValue, out var sessionId) ||
                !Guid.TryParse(phoneIdValue, out var phoneId) ||
                driverId == Guid.Empty ||
                sessionId == Guid.Empty ||
                phoneId == Guid.Empty)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "JWT 身分資訊無效"
                });
            }

            // ==========================================
            // 3. 確認 DPoP Middleware 已完成驗證
            // ==========================================

            if (HttpContext.Items["DpopValidated"] is not true)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "DPoP 尚未通過驗證"
                });
            }

            // ==========================================
            // 4. 檢查 Refresh Token
            // ==========================================

            if (request == null ||
                string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Refresh Token 不可為空"
                });
            }

            // ==========================================
            // 5. 執行登出
            // ==========================================

            try
            {
                bool success =
                    await _logoutService.LogoutAsync(
                        request.RefreshToken,
                        driverId,
                        sessionId,
                        phoneId,
                        cancellationToken);

                if (!success)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Refresh Token 無效，或與目前登入 Session 不一致"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "登出成功，Session 已撤銷"
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機登出時發生錯誤，DriverId={DriverId}, SessionId={SessionId}",
                    driverId,
                    sessionId);

                return StatusCode(500, new
                {
                    success = false,
                    message = "登出服務發生錯誤"
                });
            }
        }

    }
}
