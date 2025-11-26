// =============================================
// File: TransactionRepository.cs
// =============================================
using EvCharge.Api.Domain;
using MongoDB.Driver;

namespace EvCharge.Api.Repositories
{
    public class TransactionRepository
    {
        private readonly IMongoCollection<Transaction> _txns;

        public TransactionRepository(IConfiguration config)
        {
            var client   = new MongoClient(config["Mongo:ConnectionString"]);
            var database = client.GetDatabase(config["Mongo:DatabaseName"]);
            _txns        = database.GetCollection<Transaction>("Transactions");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            var keys = Builders<Transaction>.IndexKeys
                .Ascending(t => t.AccountId)
                .Ascending(t => t.EffectiveOnUtc);
            _txns.Indexes.CreateOne(new CreateIndexModel<Transaction>(keys, new CreateIndexOptions { Name = "ix_txn_account_date" }));
        }

        public Task CreateAsync(Transaction t) => _txns.InsertOneAsync(t);

        public Task<List<Transaction>> GetForAccountAsync(string accountId, DateTime? from, DateTime? to, TxnType? type)
        {
            var filter = Builders<Transaction>.Filter.Where(t => t.AccountId == accountId);

            if (from.HasValue)
                filter &= Builders<Transaction>.Filter.Gte(t => t.EffectiveOnUtc, from.Value);
            if (to.HasValue)
                filter &= Builders<Transaction>.Filter.Lte(t => t.EffectiveOnUtc, to.Value);
            if (type.HasValue)
                filter &= Builders<Transaction>.Filter.Eq(t => t.TxnType, type.Value);

            return _txns.Find(filter).SortBy(t => t.EffectiveOnUtc).ToListAsync();
        }

        // For reversal: all interest credits belonging to a posted batch for a given type+quarter
        public Task<List<Transaction>> GetInterestCreditsForBatchAsync(string typeCode, string quarterKey, DateTime quarterEndUtc)
        {
            var filter = Builders<Transaction>.Filter.Where(t =>
                t.TypeCode == typeCode &&
                t.TxnType == TxnType.InterestCredit &&
                t.EffectiveOnUtc == quarterEndUtc &&
                t.Narration == $"Quarterly interest {quarterKey}"
            );

            return _txns.Find(filter).ToListAsync();
        }
    }
}
