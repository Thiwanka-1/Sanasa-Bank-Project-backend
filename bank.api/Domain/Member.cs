// =============================================
// File: Member.cs
// Description: Bank member/non-member aggregate
// Notes:
//  - Stored MemberId is canonical account number for ALL their accounts
//  - Stored MemberId includes prefix: Member=100*, NonMember=200*
// =============================================
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharge.Api.Domain
{
    public enum MemberStatus
    {
        Active = 1,
        Inactive = 2
    }

    public enum PartyType
    {
        Member = 1,     // stored MemberId starts with 100
        NonMember = 2   // stored MemberId starts with 200
    }

    public class Member
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Canonical identifier used also as "account number" for all their products
        // Example: entered "1" -> stored "1001" (Member) or "2001" (NonMember)
        [BsonElement("memberId")]
        public string MemberId { get; set; } = string.Empty;

        [BsonElement("type")]
        public PartyType Type { get; set; } = PartyType.Member;

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("address")]
        public string Address { get; set; } = string.Empty;

        [BsonElement("status")]
        public MemberStatus Status { get; set; } = MemberStatus.Active;
    }
}
