// =============================================
// File: MemberRepository.cs
// =============================================
using EvCharge.Api.Domain;
using MongoDB.Driver;

namespace EvCharge.Api.Repositories
{
    public class MemberRepository
    {
        private readonly IMongoCollection<Member> _members;

        public MemberRepository(IConfiguration config)
        {
            var client   = new MongoClient(config["Mongo:ConnectionString"]);
            var database = client.GetDatabase(config["Mongo:DatabaseName"]);
            _members     = database.GetCollection<Member>("Members");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            var keys   = Builders<Member>.IndexKeys.Ascending(m => m.MemberId);
            var options= new CreateIndexOptions { Unique = true, Name = "ux_members_memberId" };
            _members.Indexes.CreateOne(new CreateIndexModel<Member>(keys, options));
        }

        public Task<List<Member>> GetAllAsync() =>
            _members.Find(_ => true).ToListAsync();

        public Task<List<Member>> GetByStatusAsync(MemberStatus status) =>
            _members.Find(m => m.Status == status).ToListAsync();

        public Task<List<Member>> GetByTypeAsync(PartyType type) =>
            _members.Find(m => m.Type == type).ToListAsync();

        public Task<Member?> GetByMongoIdAsync(string id) =>
            _members.Find(m => m.Id == id).FirstOrDefaultAsync();

        public Task<Member?> GetByMemberIdAsync(string memberId) =>
            _members.Find(m => m.MemberId == memberId).FirstOrDefaultAsync();

        public Task CreateAsync(Member m) =>
            _members.InsertOneAsync(m);

        public Task UpdateAsync(string memberId, Member updated) =>
            _members.ReplaceOneAsync(m => m.MemberId == memberId, updated);

        public Task DeleteByMemberIdAsync(string memberId) =>
            _members.DeleteOneAsync(m => m.MemberId == memberId);

        public Task SetStatusAsync(string memberId, MemberStatus status) =>
            _members.UpdateOneAsync(
                Builders<Member>.Filter.Eq(m => m.MemberId, memberId),
                Builders<Member>.Update.Set(m => m.Status, status)
            );
    }
}
