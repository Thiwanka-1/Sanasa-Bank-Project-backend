// =============================================
// File: SecurityService.cs
// Description: Password hashing (PBKDF2 + salt) and JWT generation
// Notes:
//  - Backward compatible verification for old SHA-256 base64 hashes
//  - JWT includes NameIdentifier (user Id) so self-only checks work
// Author: Gamithu / IT22295224 (updated)
// =============================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EvCharge.Api.Services
{
    public class SecurityService
    {
        private readonly IConfiguration _config;
        public SecurityService(IConfiguration config) => _config = config;

        // -------------------------------
        // Password hashing (PBKDF2 + salt)
        // Format: PBKDF2$<iterations>$<saltB64>$<hashB64>
        // -------------------------------
        private const int SaltSize = 16;            // 128-bit salt
        private const int KeySize  = 32;            // 256-bit key
        private const int DefaultIterations = 100_000; // ~100k (tune as needed)

        public string HashPassword(string password)
        {
            // PBKDF2 with random salt
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, DefaultIterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(KeySize);

            return $"PBKDF2${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public bool VerifyPassword(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return false;

            // New format?
            if (stored.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                var parts = stored.Split('$');
                if (parts.Length != 4) return false;

                if (!int.TryParse(parts[1], out var iterations)) return false;
                var salt = Convert.FromBase64String(parts[2]);
                var key  = Convert.FromBase64String(parts[3]);

                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
                var candidate = pbkdf2.GetBytes(key.Length);

                return CryptographicOperations.FixedTimeEquals(candidate, key);
            }

            // Legacy fallback: simple SHA-256 Base64 (your old approach)
            // NOTE: Only for backward-compat. New writes should use PBKDF2 via HashPassword().
            using var sha256 = SHA256.Create();
            var legacyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var legacyB64   = Convert.ToBase64String(legacyBytes);
            return string.Equals(legacyB64, stored, StringComparison.Ordinal);
        }

        // -------------------------------
        // JWT generation (no roles)
        // Claims included:
        //   - sub: user Id (subject)
        //   - nameidentifier: user Id (for [Authorize] self checks)
        //   - jti: unique token id
        //   - iat: issued-at (epoch seconds)
        // -------------------------------
        public string CreateJwtToken(string userId, string _unusedRole, out DateTime expiresUtc)
        {
            var issuer  = _config["Jwt:Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer");
            var audience= _config["Jwt:Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience");
            var key     = _config["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");

            var minutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 120;
            expiresUtc = DateTime.UtcNow.AddMinutes(minutes);

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds       = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var now = DateTimeOffset.UtcNow;
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresUtc,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
