// =============================================
// File: AccountTypeRepository.cs
// Description: Mongo data access for AccountType (catalog)
// Notes:
//  - Ensures unique index on TypeId
//  - Totals are updated by Account/Transaction services (not by admin endpoints)
// =============================================
using EvCharge.Api.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EvCharge.Api.Repositories
{
    public class AccountTypeRepository
    {
        private readonly IMongoCollection<AccountType> _types;

        public AccountTypeRepository(IConfiguration config)
        {
            var client   = new MongoClient(config["Mongo:ConnectionString"]);
            var database = client.GetDatabase(config["Mongo:DatabaseName"]);
            _types       = database.GetCollection<AccountType>("AccountTypes");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            var keys    = Builders<AccountType>.IndexKeys.Ascending(t => t.TypeId);
            var options = new CreateIndexOptions { Unique = true, Name = "ux_accounttypes_typeid" };
            _types.Indexes.CreateOne(new CreateIndexModel<AccountType>(keys, options));
        }

        public Task<List<AccountType>> GetAllAsync() =>
            _types.Find(_ => true).ToListAsync();

        public Task<AccountType?> GetByTypeIdAsync(string typeId) =>
            _types.Find(t => t.TypeId == typeId).FirstOrDefaultAsync();

        public Task<AccountType?> GetByObjectIdAsync(string id) =>
            _types.Find(t => t.Id == id).FirstOrDefaultAsync();

        public async Task CreateAsync(AccountType entity)
        {
            await _types.InsertOneAsync(entity);
        }

        public Task UpdateAsync(string typeId, AccountType updated) =>
            _types.ReplaceOneAsync(t => t.TypeId == typeId, updated);

        public Task SetActiveAsync(string typeId, bool isActive) =>
            _types.UpdateOneAsync(
                Builders<AccountType>.Filter.Eq(t => t.TypeId, typeId),
                Builders<AccountType>.Update.Set(t => t.IsActive, isActive)
            );

        // System-side: totals maintenance hooks (called by accounts/transactions later)
        public Task UpdateTotalsAsync(string typeId, decimal totalBalanceDelta, decimal interestPaidDelta) =>
            _types.UpdateOneAsync(
                Builders<AccountType>.Filter.Eq(t => t.TypeId, typeId),
                Builders<AccountType>.Update
                    .Inc(t => t.TotalBalanceAllAccounts, totalBalanceDelta)
                    .Inc(t => t.TotalInterestPaidToDate, interestPaidDelta)
            );
    }
}
