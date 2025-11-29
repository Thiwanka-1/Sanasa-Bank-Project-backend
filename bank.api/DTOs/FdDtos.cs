// =============================================
// File: FdDtos.cs
// Description: DTOs for Fixed Deposit flows
// =============================================
using System.ComponentModel.DataAnnotations;

namespace EvCharge.Api.DTOs
{
    public class OpenFixedDepositRequest
    {
        [Required] public string MemberId { get; set; } = string.Empty;     // e.g., "1001"
        [Required] public string TypeCode { get; set; } = "FD";             // FD product code
        [Required] [Range(0.01, double.MaxValue)] public decimal Principal { get; set; }
        // If not provided, we use AccountType.Attributes.DefaultTenorDays
        public int? TenorDays { get; set; }
        // Savings account type where maturity/premature payouts go (e.g., "A1")
        [Required] public string SavingsTypeCode { get; set; } = "A1";
    }

    public class PrematureCloseFixedDepositRequest
    {
        [Required] public string MemberId { get; set; } = string.Empty;
        [Required] public string TypeCode { get; set; } = "FD";
        [Required] public string SavingsTypeCode { get; set; } = "A1";
    }

    public enum FdRenewalMode
    {
        PrincipalOnly = 1,          // interest goes to savings; principal stays principal
        PrincipalPlusInterest = 2   // interest added to principal for new term
    }

    public class MatureOrRenewFixedDepositRequest
    {
        [Required] public string MemberId { get; set; } = string.Empty;
        [Required] public string TypeCode { get; set; } = "FD";
        [Required] public string SavingsTypeCode { get; set; } = "A1";

        // "withdraw" OR "renew"
        [Required] public string Action { get; set; } = "withdraw";

        // For renew:
        public int? TenorDays { get; set; }                 // new term
        public FdRenewalMode? RenewalMode { get; set; }     // PrincipalOnly | PrincipalPlusInterest
    }


    public class FdAccountResponse
    {
        public string Id { get; set; } = string.Empty;
        public string MemberId { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;
        public EvCharge.Api.Domain.AccountCategory Category { get; set; }
        public EvCharge.Api.Domain.AccountStatus Status { get; set; }
        public decimal PrincipalBalance { get; set; }
        public decimal AccruedInterest { get; set; }
        public DateTime OpenedOnUtc { get; set; }
        public DateTime? MaturityOnUtc { get; set; }
        public string? LinkedSavingsAccountNumber { get; set; }
        public Dictionary<string, object>? Attributes { get; set; }   // plain CLR
    }

     public class FdMaturityPreviewResponse
    {
        public string MemberId { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;

        public decimal Principal { get; set; }
        public int TenorDays { get; set; }
        public decimal AnnualRate { get; set; }

        public decimal InterestAtMaturity { get; set; }
        public decimal PayoutAtMaturity { get; set; }

        public DateTime OpenedOnUtc { get; set; }
        public DateTime? MaturityOnUtc { get; set; }
    }

    public class FdPrematurePreviewResponse
    {
        public string MemberId { get; set; } = string.Empty;
        public string TypeCode { get; set; } = string.Empty;

        public decimal Principal { get; set; }
        public int PrematureThresholdMonths { get; set; }
        public decimal PrematureAnnualRate { get; set; }
        public int ElapsedDays { get; set; }
        public bool EligibleForPrematureInterest { get; set; }

        public decimal InterestIfClosed { get; set; }  // 0 if ineligible
        public decimal PayoutIfClosed { get; set; }    // principal + interestIfClosed

        public DateTime OpenedOnUtc { get; set; }
        public DateTime AsOfUtc { get; set; }
    }
}
