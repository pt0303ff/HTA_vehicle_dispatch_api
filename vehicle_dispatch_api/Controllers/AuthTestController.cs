
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace vehicle_dispatch_api.Controllers
{
    // ==========================================
    // JWT 認證測試 Controller
    // ==========================================

    [ApiController]
    [Route("api/test/auth")]

    // 此 Controller 所有 API 都必須通過 JWT 驗證
    [Authorize]

    public class AuthTestController : ControllerBase
    {
        // ==========================================
        // GET: api/test/auth/me
        // 測試 JWT 是否有效
        // ==========================================

        [HttpGet("me")]
        public IActionResult Me()
        {
            // 從 JWT Claims 取得登入資訊

            var driverId =
                User.FindFirst("sub")?.Value;

            var sessionId =
                User.FindFirst("session_id")?.Value;

            var phoneId =
                User.FindFirst("phone_id")?.Value;

            var tokenType =
                User.FindFirst("token_type")?.Value;


            // JWT 驗證通過後回傳資訊

            return Ok(new
            {
                success = true,

                message = "JWT 驗證成功",

                driverId,

                sessionId,

                phoneId,

                tokenType
            });
        }
    }
}
