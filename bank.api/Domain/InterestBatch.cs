// =============================================
// File: InterestBatch.cs
// Description: Records posted quarterly interest to prevent duplicates
// =============================================
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharge.Api.Domain
{
    public class InterestBatch
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("typeCode")]
        public string TypeCode { get; set; } = string.Empty;

        // e.g., "2025Q2"
        [BsonElement("quarterKey")]
        public string QuarterKey { get; set; } = string.Empty;

        [BsonElement("periodStartUtc")]
        public DateTime PeriodStartUtc { get; set; }

        [BsonElement("periodEndUtc")]
        public DateTime PeriodEndUtc { get; set; }

        [BsonElement("postedAtUtc")]
        public DateTime PostedAtUtc { get; set; }

        [BsonElement("totalAccounts")]
        public int TotalAccounts { get; set; }

        [BsonElement("totalInterest")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TotalInterest { get; set; }

        [BsonElement("postedByUserId")]
        public string PostedByUserId { get; set; } = string.Empty;

        // Reversal metadata (optional)
        [BsonElement("isReversed")]
        public bool IsReversed { get; set; } = false;

        [BsonElement("reversedAtUtc")]
        public DateTime? ReversedAtUtc { get; set; }

        [BsonElement("reversedByUserId")]
        public string? ReversedByUserId { get; set; }
    }
}
