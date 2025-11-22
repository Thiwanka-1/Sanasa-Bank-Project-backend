// =============================================
// File: TransactionDtos.cs
// =============================================
using System.ComponentModel.DataAnnotations;

namespace EvCharge.Api.DTOs
{
    public class DepositRequest
    {
        [Required]
        public string MemberId { get; set; } = string.Empty;

        [Required]
        public string TypeCode { get; set; } = string.Empty;

        [Required, Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }

        [MaxLength(200)]
        public string? Narration { get; set; } = "Deposit";
    }

    public class WithdrawRequest
    {
        [Required]
        public string MemberId { get; set; } = string.Empty;

        [Required]
        public string TypeCode { get; set; } = string.Empty;

        [Required, Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }

        [MaxLength(200)]
        public string? Narration { get; set; } = "Withdrawal";
    }

    public class TxnQuery
    {
        public DateTime? DateFromUtc { get; set; }
        public DateTime? DateToUtc { get; set; }
        public string? Type { get; set; } // "Deposit", "Withdrawal", etc. optional
    }
}
