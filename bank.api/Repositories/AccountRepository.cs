// =============================================
// File: AccountRepository.cs
// Notes:
//  - Unique index on (MemberId, TypeCode)
// =============================================
using EvCharge.Api.Domain;
using MongoDB.Driver;

namespace EvCharge.Api.Repositories
{
    public class AccountRepository
    {
        private readonly IMongoCollection<Account> _accounts;

        public AccountRepository(IConfiguration config)
        {
            var client   = new MongoClient(config["Mongo:ConnectionString"]);
            var database = client.GetDatabase(config["Mongo:DatabaseName"]);
            _accounts    = database.GetCollection<Account>("Accounts");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            var keys = Builders<Account>.IndexKeys
                .Ascending(a => a.MemberId)
                .Ascending(a => a.TypeCode);
            var options = new CreateIndexOptions { Unique = true, Name = "ux_accounts_member_type" };
            _accounts.Indexes.CreateOne(new CreateIndexModel<Account>(keys, options));
        }

        public Task<Account?> GetAsync(string memberId, string typeCode) =>
            _accounts.Find(a => a.MemberId == memberId && a.TypeCode == typeCode).FirstOrDefaultAsync();

        public Task<List<Account>> GetByMemberAsync(string memberId) =>
            _accounts.Find(a => a.MemberId == memberId).ToListAsync();

        public Task CreateAsync(Account acc) => _accounts.InsertOneAsync(acc);

        public Task UpdateAsync(Account acc) =>
            _accounts.ReplaceOneAsync(a => a.Id == acc.Id, acc);

        public Task SetStatusAsync(string memberId, string typeCode, AccountStatus status) =>
            _accounts.UpdateOneAsync(
                Builders<Account>.Filter.Where(a => a.MemberId == memberId && a.TypeCode == typeCode),
                Builders<Account>.Update.Set(a => a.Status, status)
            );

        // When Member becomes inactive/active → cascade their accounts
        public Task SetStatusForMemberAsync(string memberId, AccountStatus status) =>
            _accounts.UpdateManyAsync(
                Builders<Account>.Filter.Where(a => a.MemberId == memberId),
                Builders<Account>.Update.Set(a => a.Status, status)
            );
    }
}
