// =============================================
// File: InterestDtos.cs
// =============================================
using System.ComponentModel.DataAnnotations;

namespace EvCharge.Api.DTOs
{
    public class InterestPreviewRequest
    {
        [Required] public string TypeCode { get; set; } = string.Empty;  // e.g. "A1"
        [Required, RegularExpression(@"^\d{4}Q[1-4]$")] public string Quarter { get; set; } = string.Empty; // "2025Q2"
    }

    public class InterestRunRequest
    {
        [Required] public string TypeCode { get; set; } = string.Empty;
        [Required, RegularExpression(@"^\d{4}Q[1-4]$")] public string Quarter { get; set; } = string.Empty;
    }

    public class InterestReverseRequest
    {
        [Required] public string TypeCode { get; set; } = string.Empty;
        [Required, RegularExpression(@"^\d{4}Q[1-4]$")] public string Quarter { get; set; } = string.Empty;
    }

    public class InterestPreviewItem
    {
        public string MemberId { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;
        public decimal MinBalanceInQuarter { get; set; }
        public decimal InterestAmount { get; set; }
    }

    public class InterestPreviewResponse
    {
        public string TypeCode { get; set; } = string.Empty;
        public string Quarter { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public List<InterestPreviewItem> Items { get; set; } = new();
        public int AccountCount => Items.Count;
        public decimal TotalInterest => Items.Sum(i => i.InterestAmount);
    }

    public class InterestBatchDto
    {
        public string Id { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;
        public string Quarter { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public DateTime PostedAtUtc { get; set; }
        public int TotalAccounts { get; set; }
        public decimal TotalInterest { get; set; }
        public bool IsReversed { get; set; }
        public DateTime? ReversedAtUtc { get; set; }
    }
}
