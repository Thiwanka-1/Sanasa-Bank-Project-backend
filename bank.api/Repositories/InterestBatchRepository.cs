// =============================================
// File: InterestBatchRepository.cs
// =============================================
using EvCharge.Api.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EvCharge.Api.Repositories
{
    public class InterestBatchRepository
    {
        private readonly IMongoCollection<InterestBatch> _batches;

        public InterestBatchRepository(IConfiguration config)
        {
            var client   = new MongoClient(config["Mongo:ConnectionString"]);
            var database = client.GetDatabase(config["Mongo:DatabaseName"]);
            _batches     = database.GetCollection<InterestBatch>("InterestBatches");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            // ---- Remove legacy strict unique index if it exists ----
            // (Earlier versions used a full-unique index that blocked re-posting after reversal.)
            var existing = _batches.Indexes.List().ToList();
            var legacy = existing.FirstOrDefault(ix =>
            {
                var name = ix.GetValue("name", default);
                return name.IsString && name.AsString == "ux_interestBatch_type_quarter";
            });
            if (legacy != null)
            {
                try { _batches.Indexes.DropOne("ux_interestBatch_type_quarter"); } catch { /* ignore */ }
            }

            // ---- Partial UNIQUE index: only one *active* batch per (TypeCode, QuarterKey) ----
            var keys = Builders<InterestBatch>.IndexKeys
                .Ascending(b => b.TypeCode)
                .Ascending(b => b.QuarterKey);

            var partialFilter = Builders<InterestBatch>.Filter.Eq(b => b.IsReversed, false);

            var options = new CreateIndexOptions<InterestBatch>
            {
                Name = "ux_interest_active_type_quarter",
                Unique = true,
                PartialFilterExpression = partialFilter
            };

            _batches.Indexes.CreateOne(new CreateIndexModel<InterestBatch>(keys, options));

            // Helpful sort index for listings
            _batches.Indexes.CreateOne(new CreateIndexModel<InterestBatch>(
                Builders<InterestBatch>.IndexKeys.Descending(b => b.PostedAtUtc),
                new CreateIndexOptions<InterestBatch> { Name = "ix_interest_postedAt_desc" }
            ));
        }

        /// <summary>
        /// Returns the most recently posted batch for (typeCode, quarterKey),
        /// regardless of IsReversed (active or reversed).
        /// </summary>
        public Task<InterestBatch?> GetAsync(string typeCode, string quarterKey) =>
            _batches.Find(b => b.TypeCode == typeCode && b.QuarterKey == quarterKey)
                    .SortByDescending(b => b.PostedAtUtc)
                    .FirstOrDefaultAsync();

        /// <summary>
        /// Returns the ACTIVE (non-reversed) batch for (typeCode, quarterKey), if any.
        /// </summary>
        public Task<InterestBatch?> GetActiveAsync(string typeCode, string quarterKey) =>
            _batches.Find(b => b.TypeCode == typeCode && b.QuarterKey == quarterKey && !b.IsReversed)
                    .FirstOrDefaultAsync();

        public Task<InterestBatch?> GetByIdAsync(string id) =>
            _batches.Find(b => b.Id == id).FirstOrDefaultAsync();

        public Task<List<InterestBatch>> ListAsync(string? typeCode = null)
        {
            var filter = string.IsNullOrWhiteSpace(typeCode)
                ? Builders<InterestBatch>.Filter.Empty
                : Builders<InterestBatch>.Filter.Eq(b => b.TypeCode, typeCode);

            return _batches.Find(filter)
                           .SortByDescending(b => b.PostedAtUtc)
                           .ToListAsync();
        }

        public Task CreateAsync(InterestBatch b) => _batches.InsertOneAsync(b);

        public Task MarkReversedAsync(string id, DateTime whenUtc, string userId) =>
            _batches.UpdateOneAsync(
                Builders<InterestBatch>.Filter.Eq(b => b.Id, id),
                Builders<InterestBatch>.Update
                    .Set(b => b.IsReversed, true)
                    .Set(b => b.ReversedAtUtc, whenUtc)
                    .Set(b => b.ReversedByUserId, userId)
            );
    }
}
