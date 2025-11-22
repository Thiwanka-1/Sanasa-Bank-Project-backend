// =============================================
// File: UsersController.cs
// Purpose: Minimal user CRUD with self-only protections
// Notes:
//   - Create: open (no auth) for now
//   - GetAll: any authenticated user
//   - GetById/Update/Delete: only the logged-in user's own record
// =============================================

using EvCharge.Api.Domain;
using EvCharge.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly UserRepository _repo;

        public UsersController(IConfiguration config)
        {
            _repo = new UserRepository(config);
        }

        // Simple SHA256 hash (demo). Keep as-is per your instruction.
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        // 🔹 GET ALL — any authenticated user
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<List<User>>> GetAll() =>
            await _repo.GetAllAsync();

        // 🔹 GET BY ID — only self
        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<User>> GetById(string id)
        {
            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();

            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(subject) || subject != id) return Forbid();

            return user;
        }

        // 🔹 CREATE — open (no auth) for now
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> Create([FromBody] User user)
        {
            // hash the incoming "passwordHash" field as the plain password
            user.PasswordHash = HashPassword(user.PasswordHash);

            await _repo.CreateAsync(user);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        // 🔹 UPDATE — only self
        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult> Update(string id, [FromBody] User updated)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) return NotFound();

            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(subject) || subject != id) return Forbid();

            // enforce self id
            updated.Id = id;

            // allow changing username and/or password
            if (!string.IsNullOrWhiteSpace(updated.PasswordHash))
                updated.PasswordHash = HashPassword(updated.PasswordHash);
            else
                updated.PasswordHash = existing.PasswordHash;

            if (string.IsNullOrWhiteSpace(updated.Username))
                updated.Username = existing.Username;

            await _repo.UpdateAsync(id, updated);
            return NoContent();
        }

        // 🔹 DELETE — only self
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult> Delete(string id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) return NotFound();

            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(subject) || subject != id) return Forbid();

            await _repo.DeleteAsync(id);
            return NoContent();
        }
    }
}
