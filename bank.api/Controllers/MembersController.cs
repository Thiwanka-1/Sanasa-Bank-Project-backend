// =============================================
// File: MembersController.cs
// Notes:
//  - Stored MemberId = 100/200 + entered number (entered "1" => "1001"/"2001")
//  - Stored MemberId is also the account number for ALL their accounts
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

        public MembersController(IConfiguration config)
        {
            _repo = new MemberRepository(config);
        }

        // Optional helper to normalize numeric input
        private static string BuildStoredMemberId(PartyType type, string enteredId)
        {
            // Normalize numeric (strip leading zeros via parse); keep "0" if all zeros
            var n = int.Parse(string.IsNullOrEmpty(enteredId) ? "0" : enteredId);
            var prefix = type == PartyType.Member ? "100" : "200";
            return $"{prefix}{n}";
        }

        // GET /api/members?status=Active|Inactive&partyType=Member|NonMember (both optional)
        [HttpGet]
        public async Task<ActionResult<List<MemberResponse>>> GetAll(
            [FromQuery] MemberStatus? status,
            [FromQuery] PartyType? partyType)
        {
            List<Member> list;

            if (status.HasValue && partyType.HasValue)
                list = (await _repo.GetByStatusAsync(status.Value)).Where(m => m.Type == partyType.Value).ToList();
            else if (status.HasValue)
                list = await _repo.GetByStatusAsync(status.Value);
            else if (partyType.HasValue)
                list = await _repo.GetByTypeAsync(partyType.Value);
            else
                list = await _repo.GetAllAsync();

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

        // POST /api/members
        [HttpPost]
        public async Task<ActionResult<MemberResponse>> Create([FromBody] CreateMemberRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var storedId = BuildStoredMemberId(req.Type, req.EnteredId);

            // prevent duplicates on the final stored id
            var exists = await _repo.GetByMemberIdAsync(storedId);
            if (exists != null) return Conflict(new { message = "MemberId already exists." });

            var entity = new Member
            {
                MemberId = storedId,
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
        // Only name/address are editable; MemberId + Type are immutable
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

            await _repo.SetStatusAsync(memberId, req.Status);

            // TODO when Accounts exist:
            // cascade account status and pause interest
            return Ok(new { message = $"Member {memberId} status set to {req.Status}" });
        }

        // DELETE /api/members/{memberId}
        [HttpDelete("{memberId}")]
        public async Task<IActionResult> Delete(string memberId)
        {
            var existing = await _repo.GetByMemberIdAsync(memberId);
            if (existing == null) return NotFound();

            // TODO when Accounts exist: prevent delete if accounts present
            await _repo.DeleteByMemberIdAsync(memberId);
            return NoContent();
        }
    }
}
