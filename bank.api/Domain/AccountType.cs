// =============================================
// File: AccountType.cs
// Description: Catalog entry defining a bank product
// Notes:
//  - TypeId is manual (e.g., "A1", "FD6M", "C1") and UNIQUE
//  - Totals are system-maintained (read-only in API)
// =============================================
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharge.Api.Domain
{
    public class AccountType
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("typeId")]
        public string TypeId { get; set; } = string.Empty;  // manual code, unique (e.g., "A1")

        [BsonElement("category")]
        public AccountCategory Category { get; set; } = AccountCategory.MemberDeposits;

        [BsonElement("typeName")]
        public string TypeName { get; set; } = string.Empty; // e.g., "Savings", "Fixed Deposit"

        [BsonElement("interestRateAnnual")]
        public decimal? InterestRateAnnual { get; set; } = null; // 0..1; null for Shares

        [BsonElement("interestMethod")]
        public InterestMethod InterestMethod { get; set; } = InterestMethod.None;

        [BsonElement("minimumBalance")]
        public decimal MinimumBalance { get; set; } = 0m;

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;

        // Optional type-specific attributes (stored as raw JSON/BSON)
        // e.g., FD: { "RateTable":[{"TenorDays":180,"Rate":0.055},{"TenorDays":365,"Rate":0.065}], "DefaultTenorDays":365 }
        [BsonElement("attributes")]
        public BsonDocument? Attributes { get; set; } = null;

        // System-maintained totals (read-only via API)
        [BsonElement("totalBalanceAllAccounts")]
        public decimal TotalBalanceAllAccounts { get; set; } = 0m;

        [BsonElement("totalInterestPaidToDate")]
        public decimal TotalInterestPaidToDate { get; set; } = 0m;
    }
}
