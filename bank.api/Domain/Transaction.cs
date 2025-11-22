// =============================================
// File: Transaction.cs
// Description: Immutable ledger entry for an account
// =============================================
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharge.Api.Domain
{
    public class Transaction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("accountId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AccountId { get; set; } = string.Empty;

        [BsonElement("memberId")]
        public string MemberId { get; set; } = string.Empty;

        [BsonElement("typeCode")]
        public string TypeCode { get; set; } = string.Empty;

        [BsonElement("txnType")]
        public TxnType TxnType { get; set; }

        [BsonElement("amount")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Amount { get; set; } = 0m; // always positive; sign implied by TxnType

        [BsonElement("narration")]
        public string Narration { get; set; } = string.Empty;

        [BsonElement("effectiveOnUtc")]
        public DateTime EffectiveOnUtc { get; set; } = DateTime.UtcNow;

        [BsonElement("postedByUserId")]
        public string PostedByUserId { get; set; } = string.Empty;

        // Snapshot after applying this txn
        [BsonElement("balanceAfterTxn")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal BalanceAfterTxn { get; set; } = 0m;
    }
}
