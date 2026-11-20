
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
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

        // Refresh Token 更新服務
        private readonly DriverRefreshService _refreshService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InternalAuthController> _logger;
        
        // Logout 登出服務
        private readonly DriverLogoutService _logoutService;

        public InternalAuthController(
            DriverActivationService activationService,
            DriverLoginService loginService,
            DriverRefreshService refreshService,
            DriverLogoutService logoutService,
            IConfiguration configuration,
            ILogger<InternalAuthController> logger)
        {
            _activationService = activationService;
            _loginService = loginService;
            _refreshService = refreshService;
            _logoutService = logoutService;
            _configuration = configuration;
            _logger = logger;
        }

        // POST: api/internal/auth/activate
        [HttpPost("activate")]
        public async Task<IActionResult> Activate(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,
            
            [FromBody] ActivateDriverRequest request,
            CancellationToken cancellationToken)
        {
            // 1. 取得內部服務驗證 Key
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError(
                    "尚未設定 InternalApi:ApiKey");

                return StatusCode(500, new
                {
                    success = false,
                    message = "內部服務尚未完成設定"
                });
            }

            // 2. 檢查 HTTP Header

            if (string.IsNullOrWhiteSpace(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // 3. 使用固定時間比較 Key
            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey);

            bool authorized =
                expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);

            if (!authorized)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // 4. 執行帳號開通
            try
            {
                var result = await _activationService.ActivateAsync(
                    request,
                    cancellationToken);

                // null 代表開通失敗
                if (result == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "開通資訊無效或已過期，請確認後重試或聯絡管理員"
                    });
                }

                // 開通成功，回傳司機及手機設備資訊
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
        // POST: api/internal/auth/login
        // 司機登入驗證
        // ==========================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromBody] DriverLoginRequest request,
            CancellationToken cancellationToken)
        {
            // 1. 取得內部服務驗證 Key

            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError(
                    "尚未設定 InternalApi:ApiKey");

                return StatusCode(500, new
                {
                    success = false,
                    message = "內部服務尚未完成設定"
                });
            }


            // 2. 檢查 HTTP Header

            if (string.IsNullOrWhiteSpace(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // 3. 使用固定時間比較 Key

            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey);

            bool authorized =
                expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);

            if (!authorized)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // 4. 執行司機登入驗證

            try
            {
                var result = await _loginService.LoginAsync(
                    request,
                    cancellationToken);

                if (result == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "帳號、密碼或設備資訊錯誤"
                    });
                }


                // 5. 登入成功，回傳 Session 資料

                return Ok(new
                {
                    success = true,

                    message = "登入成功",

                    // 司機資訊
                    driverId = result.DriverId,

                    // Session
                    sessionId = result.SessionId,

                    sessionIdleExpiresAt = result.SessionIdleExpiresAt,

                    sessionAbsoluteExpiresAt = result.SessionAbsoluteExpiresAt,

                    accountStatus = result.AccountStatus,

                    // JWT Access Token
                    tokenType = result.TokenType,

                    accessToken = result.AccessToken,

                    accessTokenExpiresAt = result.AccessTokenExpiresAt,

                    // Refresh Token
                    refreshToken = result.RefreshToken,

                    refreshTokenExpiresAt = result.RefreshTokenExpiresAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機登入驗證時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "登入服務發生錯誤"
                });
            }
        }


        // ==========================================
        // POST: api/internal/auth/refresh
        // 更新 JWT Access Token 及 Refresh Token
        // ==========================================

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromBody] DriverRefreshRequest request,

            CancellationToken cancellationToken)
        {
            // ==========================================
            // 1. 取得內部服務驗證 Key
            // ==========================================

            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
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
            // 2. 檢查 HTTP Header
            // ==========================================

            if (string.IsNullOrWhiteSpace(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // ==========================================
            // 3. 使用固定時間比較 Key
            // ==========================================

            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey);

            bool authorized =
                expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);

            if (!authorized)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // ==========================================
            // 4. 執行 Refresh Token 更新
            // ==========================================

            try
            {
                var result = await _refreshService.RefreshAsync(
                    request.RefreshToken,
                    cancellationToken);

                // Token 無效、已過期或 Session 已失效
                if (result == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Refresh Token 無效、已過期或 Session 已失效"
                    });
                }


                // ==========================================
                // 5. 回傳新的 Token
                // ==========================================

                return Ok(new
                {
                    success = true,

                    message = "Token 更新成功",

                    // 司機資訊
                    driverId = result.DriverId,

                    // Session
                    sessionId = result.SessionId,

                    sessionIdleExpiresAt =
                        result.SessionIdleExpiresAt,

                    sessionAbsoluteExpiresAt =
                        result.SessionAbsoluteExpiresAt,

                    // JWT Access Token
                    tokenType = result.TokenType,

                    accessToken = result.AccessToken,

                    accessTokenExpiresAt =
                        result.AccessTokenExpiresAt,

                    // 新 Refresh Token
                    refreshToken = result.RefreshToken,

                    refreshTokenExpiresAt =
                        result.RefreshTokenExpiresAt,

                    // Token 輪替版本
                    tokenVersion = result.TokenVersion
                });
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

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(
            [FromHeader(Name = "X-Internal-Api-Key")]
            string? suppliedKey,

            [FromBody] DriverLogoutRequest request,

            CancellationToken cancellationToken)
        {
            // ==========================================
            // 1. 取得內部服務驗證 Key
            // ==========================================

            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
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
            // 2. 檢查 HTTP Header
            // ==========================================

            if (string.IsNullOrWhiteSpace(suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // ==========================================
            // 3. 固定時間比較 API Key
            // ==========================================

            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey);

            bool authorized =
                expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);

            if (!authorized)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }


            // ==========================================
            // 4. 執行 Logout
            // ==========================================

            try
            {
                bool success =
                    await _logoutService.LogoutAsync(
                        request.RefreshToken,
                        cancellationToken);


                if (!success)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Refresh Token 無效"
                    });
                }


                // ==========================================
                // 5. 登出成功
                // ==========================================

                return Ok(new
                {
                    success = true,
                    message = "登出成功，Session 已撤銷"
                });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    "Logout 服務逾時");

                return StatusCode(504, new
                {
                    success = false,
                    message = "登出服務逾時"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機登出時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "登出服務發生錯誤"
                });
            }
        }



    }
}
