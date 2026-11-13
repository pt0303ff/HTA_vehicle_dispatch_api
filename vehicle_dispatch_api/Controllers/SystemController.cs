
using Microsoft.AspNetCore.Mvc;

namespace vehicle_dispatch_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new
            {
                success = true,
                message = "Vehicle Dispatch API is running",
                serverTime = DateTime.Now
            });
        }
    }
}
