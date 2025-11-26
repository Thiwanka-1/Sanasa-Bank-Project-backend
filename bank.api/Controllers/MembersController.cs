// =============================================
// File: MembersController.cs
// =============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EvCharge.Api.Repositories;
using EvCharge.Api.DTOs;
using EvCharge.Api.Domain;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MembersController : ControllerBase
    {
        private readonly MemberRepository _repo;
        private readonly AccountRepository _accounts;

        public MembersController(IConfiguration config)
        {
            _repo = new MemberRepository(config);
            _accounts = new AccountRepository(config);
        }

        // GET /api/members?status=Active|Inactive
        [HttpGet]
        public async Task<ActionResult<List<MemberResponse>>> GetAll([FromQuery] MemberStatus? status)
        {
            var list = status.HasValue ? await _repo.GetByStatusAsync(status.Value)
                                       : await _repo.GetAllAsync();

            return list.Select(m => new MemberResponse
            {
                Id = m.Id,
                MemberId = m.MemberId,
                Type = m.Type,
                Name = m.Name,
                Address = m.Address,
                Status = m.Status
            }).ToList();
        }

        // GET /api/members/{memberId}
        [HttpGet("{memberId}")]
        public async Task<ActionResult<MemberResponse>> GetByMemberId(string memberId)
        {
            var m = await _repo.GetByMemberIdAsync(memberId);
            if (m == null) return NotFound();

            return new MemberResponse
            {
                Id = m.Id,
                MemberId = m.MemberId,
                Type = m.Type,
                Name = m.Name,
                Address = m.Address,
                Status = m.Status
            };
        }

        // Helper: compute final visible MemberId
       private static string BuildMemberId(string baseId, PartyType type)
{
    if (baseId == null) throw new ArgumentException("BaseId is required.");

    // Keep only digits and trim whitespace
    var digits = baseId.Trim();
    if (digits.Length == 0 || !digits.All(char.IsDigit))
        throw new ArgumentException("BaseId must be digits only.");

    // Normalize: remove leading zeros (so "001" -> "1")
    // If everything was zeros, treat as invalid (we don't allow id 0)
    digits = digits.TrimStart('0');
    if (string.IsNullOrEmpty(digits))
        throw new ArgumentException("BaseId cannot be zero.");

    var prefix = (type == PartyType.Member) ? "100" : "200";
    return prefix + digits;   // e.g., 200 + 2 -> 2002, 100 + 15 -> 10015
}


        // POST /api/members
        [HttpPost]
        public async Task<ActionResult<MemberResponse>> Create([FromBody] CreateMemberRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Compute visible id
            string finalMemberId;
            try
            {
                finalMemberId = BuildMemberId(req.BaseId, req.Type);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // prevent duplicates
            var exists = await _repo.GetByMemberIdAsync(finalMemberId);
            if (exists != null) return Conflict(new { message = "MemberId already exists." });

            var entity = new Member
            {
                MemberId = finalMemberId,
                Type = req.Type,
                Name = req.Name,
                Address = req.Address,
                Status = MemberStatus.Active
            };

            await _repo.CreateAsync(entity);

            var dto = new MemberResponse
            {
                Id = entity.Id,
                MemberId = entity.MemberId,
                Type = entity.Type,
                Name = entity.Name,
                Address = entity.Address,
                Status = entity.Status
            };

            return CreatedAtAction(nameof(GetByMemberId), new { memberId = entity.MemberId }, dto);
        }

        // PUT /api/members/{memberId}
        [HttpPut("{memberId}")]
        public async Task<IActionResult> Update(string memberId, [FromBody] UpdateMemberRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var existing = await _repo.GetByMemberIdAsync(memberId);
            if (existing == null) return NotFound();

            existing.Name = req.Name;
            existing.Address = req.Address;

            await _repo.UpdateAsync(memberId, existing);
            return NoContent();
        }

        // PATCH /api/members/{memberId}/status
        [HttpPatch("{memberId}/status")]
        public async Task<IActionResult> ChangeStatus(string memberId, [FromBody] ChangeMemberStatusRequest req)
        {
            var existing = await _repo.GetByMemberIdAsync(memberId);
            if (existing == null) return NotFound();

            // 1) Update member status
            await _repo.SetStatusAsync(memberId, req.Status);

            // 2) Cascade to all accounts
            var targetStatus = req.Status == MemberStatus.Active ? AccountStatus.Active : AccountStatus.Inactive;
            await _accounts.SetStatusForMemberAsync(memberId, targetStatus);

            return Ok(new { message = $"Member {memberId} status set to {req.Status} and cascaded to accounts ({targetStatus})." });
        }

        // DELETE /api/members/{memberId}
        [HttpDelete("{memberId}")]
        public async Task<IActionResult> Delete(string memberId)
        {
            var existing = await _repo.GetByMemberIdAsync(memberId);
            if (existing == null) return NotFound();

            // TODO: prevent delete if accounts exist (or soft delete)
            await _repo.DeleteByMemberIdAsync(memberId);
            return NoContent();
        }
    }
}
