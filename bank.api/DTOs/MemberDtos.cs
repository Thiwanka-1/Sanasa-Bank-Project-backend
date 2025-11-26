// =============================================
// File: MemberDtos.cs
// =============================================
using System.ComponentModel.DataAnnotations;
using EvCharge.Api.Domain;

namespace EvCharge.Api.DTOs
{
    public class CreateMemberRequest
    {
        // e.g., "1" or "001" → we will pad to at least 3 digits
        [Required, RegularExpression(@"^\d{1,}$", ErrorMessage = "BaseId must be digits only.")]
        public string BaseId { get; set; } = string.Empty;

        // Member or NonMember
        [Required]
        public PartyType Type { get; set; }

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
        public string MemberId { get; set; } = string.Empty;
        public PartyType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public MemberStatus Status { get; set; }
    }
}
