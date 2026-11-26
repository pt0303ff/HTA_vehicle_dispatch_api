
using System;
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverLoginRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public Guid? PhoneId { get; set; }
    }
}
