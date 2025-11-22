// =============================================
// File: AuthDtos.cs
// Description: DTOs for simple username/password auth (no roles)
// Notes:
//  - Data annotations enforce basic validation in Swagger/model binding.
//  - 'Role' kept for backward compatibility but not used.
// =============================================

using System.ComponentModel.DataAnnotations;

namespace EvCharge.Api.DTOs
{
    public class SystemLoginRequest
    {
        [Required, MinLength(3), MaxLength(64)]
        public string Username { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(128)]
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Ignored in this system. Kept only for backward compatibility.
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp when this token expires.
        /// </summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>
        /// Helpful for clients; typically "Bearer".
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// Convenience field so the client can start an expiry timer without parsing dates.
        /// </summary>
        public int ExpiresInSeconds { get; set; }
    }
}
