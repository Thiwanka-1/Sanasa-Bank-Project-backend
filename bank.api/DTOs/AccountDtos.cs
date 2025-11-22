// =============================================
// File: AccountDtos.cs
// =============================================
using System.ComponentModel.DataAnnotations;
using EvCharge.Api.Domain;

namespace EvCharge.Api.DTOs
{
    public class OpenAccountRequest
    {
        [Required]
        public string MemberId { get; set; } = string.Empty;      // stored 100*/200* id

        [Required, RegularExpression(@"^[A-Za-z0-9\-]{1,12}$")]
        public string TypeCode { get; set; } = string.Empty;      // e.g., "A1"

        [Range(0, double.MaxValue)]
        public decimal InitialDeposit { get; set; } = 0m;
    }

    public class AccountResponse
    {
        public string Id { get; set; } = string.Empty;
        public string MemberId { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;
        public AccountCategory Category { get; set; }
        public AccountStatus Status { get; set; }
        public decimal PrincipalBalance { get; set; }
        public decimal AccruedInterest { get; set; }
        public DateTime OpenedOnUtc { get; set; }
    }

    public class ChangeAccountStatusRequest
    {
        [Required]
        public AccountStatus Status { get; set; }
    }
}
