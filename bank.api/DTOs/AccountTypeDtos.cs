// =============================================
// File: AccountTypeDtos.cs
// Description: DTOs for AccountType admin
// =============================================
using System.ComponentModel.DataAnnotations;
using EvCharge.Api.Domain;

namespace EvCharge.Api.DTOs
{
    public class CreateAccountTypeRequest
    {
        [Required, RegularExpression(@"^[A-Za-z0-9\-]{1,12}$", ErrorMessage = "TypeId must be 1-12 letters/digits/hyphen.")]
        public string TypeId { get; set; } = string.Empty;

        [Required]
        public AccountCategory Category { get; set; }

        [Required, MinLength(2), MaxLength(120)]
        public string TypeName { get; set; } = string.Empty;

        // 0..1 (e.g., 0.12 for 12%), or null for types without interest (e.g., Shares)
        [Range(0, 1)]
        public decimal? InterestRateAnnual { get; set; }

        [Required]
        public InterestMethod InterestMethod { get; set; }

        [Range(0, double.MaxValue)]
        public decimal MinimumBalance { get; set; } = 0m;

        // Optional free-form attributes (sent as JSON). Example for FD rate table:
        // { "RateTable":[{"TenorDays":180,"Rate":0.055},{"TenorDays":365,"Rate":0.065}], "DefaultTenorDays":365 }
        public object? Attributes { get; set; } = null;
    }

    public class UpdateAccountTypeRequest
    {
        // TypeId & Category are immutable by policy
        [Required, MinLength(2), MaxLength(120)]
        public string TypeName { get; set; } = string.Empty;

        [Range(0, 1)]
        public decimal? InterestRateAnnual { get; set; }

        [Required]
        public InterestMethod InterestMethod { get; set; }

        [Range(0, double.MaxValue)]
        public decimal MinimumBalance { get; set; } = 0m;

        public object? Attributes { get; set; } = null;
    }

    public class ChangeAccountTypeStatusRequest
    {
        [Required]
        public bool IsActive { get; set; }
    }

    public class AccountTypeResponse
    {
        public string Id { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public AccountCategory Category { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public decimal? InterestRateAnnual { get; set; }
        public InterestMethod InterestMethod { get; set; }
        public decimal MinimumBalance { get; set; }
        public bool IsActive { get; set; }
        public object? Attributes { get; set; }               // plain object for Swagger
        public decimal TotalBalanceAllAccounts { get; set; }
        public decimal TotalInterestPaidToDate { get; set; }
    }
}
