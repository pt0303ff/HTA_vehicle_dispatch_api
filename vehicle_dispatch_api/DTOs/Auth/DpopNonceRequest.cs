
using System;
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DpopNonceRequest
    {
        [Required]
        public Guid PhoneId { get; set; }
    }
}
