// =============================================
// File: AuthController.cs
// Purpose: Simple auth for 2-user system (no roles)
// Endpoints:
//   POST /api/auth/login  -> get JWT
//   POST /api/auth/logout -> ok (client should discard token)
// =============================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EvCharge.Api.Services;
using EvCharge.Api.DTOs;
using EvCharge.Api.Repositories;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SecurityService _security;
        private readonly UserRepository _userRepo;

        public AuthController(SecurityService security, IConfiguration config)
        {
            _security = security;
            _userRepo = new UserRepository(config);
        }

        // POST: /api/auth/login
        // Body: { "username": "...", "password": "..." }
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] SystemLoginRequest request)
        {
            if (!ModelState.IsValid ||
                string.IsNullOrWhiteSpace(request?.Username) ||
                string.IsNullOrWhiteSpace(request?.Password))
            {
                return BadRequest(new { message = "Username and password are required." });
            }

            var user = await _userRepo.GetByUsernameAsync(request.Username);
            if (user == null)
                return Unauthorized(new { message = "Invalid credentials." });

            // 🔐 Verify with PBKDF2 (SecurityService handles legacy SHA-256 too)
            var ok = _security.VerifyPassword(request.Password, user.PasswordHash);
            if (!ok)
                return Unauthorized(new { message = "Invalid credentials." });

            var token = _security.CreateJwtToken(user.Id, string.Empty, out var exp);

            return Ok(new AuthResponse
            {
                AccessToken = token,
                Role = string.Empty, // kept for DTO compatibility; not used
                ExpiresAtUtc = exp,
                TokenType = "Bearer",
                ExpiresInSeconds = (int)(exp - DateTime.UtcNow).TotalSeconds
            });
        }

        // POST: /api/auth/logout
        // JWT is stateless; server doesn't need to do anything.
        // Client should delete the token. We just return 200 OK.
        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            return Ok(new { message = "Logged out. Please discard your token on the client." });
        }
    }
}
