// =============================================
// File: Account.cs
// Description: Bank account instance tied to a MemberId and an AccountType
// Rules:
//  - One account per (MemberId, TypeCode)
//  - MemberId is the canonical account number for all products of that person
// =============================================
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharge.Api.Domain
{
    public enum AccountStatus
    {
        Active = 1,
        Inactive = 2,
        Closed = 3
    }

    public class Account
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Canonical owner/account number (100* / 200*)
        [BsonElement("memberId")]
        public string MemberId { get; set; } = string.Empty;

        // Reference to AccountType (manual code like "A1")
        [BsonElement("typeCode")]
        public string TypeCode { get; set; } = string.Empty;

        [BsonElement("category")]
        public AccountCategory Category { get; set; }

        [BsonElement("status")]
        public AccountStatus Status { get; set; } = AccountStatus.Active;

        // Running balances
        [BsonElement("principalBalance")]
        public decimal PrincipalBalance { get; set; } = 0m;

        [BsonElement("accruedInterest")]
        public decimal AccruedInterest { get; set; } = 0m;

        [BsonElement("openedOnUtc")]
        public DateTime OpenedOnUtc { get; set; } = DateTime.UtcNow;

        [BsonElement("lastInterestCalcOnUtc")]
        public DateTime? LastInterestCalcOnUtc { get; set; } = null;

        // For FD only (reserved)
        [BsonElement("maturityOnUtc")]
        public DateTime? MaturityOnUtc { get; set; } = null;

        // For FD maturity payout target (MemberId of savings—same person usually)
        [BsonElement("linkedSavingsAccountNumber")]
        public string? LinkedSavingsAccountNumber { get; set; } = null;

        // Free-form per-type runtime values
        [BsonElement("attributes")]
        public BsonDocument? Attributes { get; set; } = null;
    }
}
