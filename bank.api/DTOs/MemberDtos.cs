// =============================================
// File: MemberDtos.cs
// =============================================
using System.ComponentModel.DataAnnotations;
using EvCharge.Api.Domain;

namespace EvCharge.Api.DTOs
{
    // Client types "raw" numeric id they want (e.g., "1", "23", or "001").
    // Server stores with 100/200 prefix based on Type.
    public class CreateMemberRequest
    {
        [Required]
        public PartyType Type { get; set; } = PartyType.Member;

        // Accept 1–6 digits; no spaces; we’ll normalize server-side
        [Required, RegularExpression(@"^\d{1,6}$", ErrorMessage = "EnteredId must be 1–6 digits (e.g., 1, 23, 001).")]
        public string EnteredId { get; set; } = string.Empty;

        [Required, MinLength(2), MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required, MinLength(3), MaxLength(240)]
        public string Address { get; set; } = string.Empty;
    }

    public class UpdateMemberRequest
    {
        [Required, MinLength(2), MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required, MinLength(3), MaxLength(240)]
        public string Address { get; set; } = string.Empty;
    }

    public class ChangeMemberStatusRequest
    {
        [Required]
        public MemberStatus Status { get; set; }
    }

    public class MemberResponse
    {
        public string Id { get; set; } = string.Empty;
        public string MemberId { get; set; } = string.Empty;  // stored 100*/200* form
        public PartyType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public MemberStatus Status { get; set; }
    }
}
