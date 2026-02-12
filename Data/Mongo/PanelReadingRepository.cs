using Shared.Models;
using MongoDB.Driver;

namespace Data.Mongo
{
    public class PanelReadingRepository : IPanelReadingRepository
    {
        private readonly IMongoCollection<PanelReading> _collection;

        public PanelReadingRepository(IMongoClient client)
        {
            var db = client.GetDatabase("MyDb");
            _collection = db.GetCollection<PanelReading>("PanelReadings");
        }

        public async Task AddAsync(PanelReading user)
        {
            await _collection.InsertOneAsync(user);
        }

        public async Task AddAsync(IEnumerable<PanelReading> panelReadings)
        {
            var options = new InsertManyOptions
            {
                IsOrdered = false
            };

            try
            {
                await _collection.InsertManyAsync(panelReadings, options);
            }
            catch (MongoBulkWriteException<PanelReading> ex)
            {
                var duplicateErrors = ex.WriteErrors
                    .Where(e => e.Category == ServerErrorCategory.DuplicateKey)
                    .ToList();

                if (duplicateErrors.Count < ex.WriteErrors.Count)
                    throw;
            }
        }

        public async Task<IEnumerable<PanelReading>> GetPanelReadingsByPanelId(int panelId, DateTime? startDate, DateTime? endDate)
        {
            var filterBuilder = Builders<PanelReading>.Filter;
            var filter = filterBuilder.Eq(x => x.PanelId, panelId);

            if (startDate.HasValue)
                filter &= filterBuilder.Gte(x => x.ReadingTime, startDate.Value);
            if (endDate.HasValue)
                filter &= filterBuilder.Lte(x => x.ReadingTime, endDate.Value);

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<Dictionary<int, List<PanelReading>>> GetPanelReadingsByPanelIds(IEnumerable<int> panelIds, DateTime? startDate, DateTime? endDate)
        {
            var filterBuilder = Builders<PanelReading>.Filter;
            var filter = filterBuilder.In(x => x.PanelId, panelIds);

            if (startDate.HasValue)
                filter &= filterBuilder.Gte(x => x.ReadingTime, startDate.Value);
            if (endDate.HasValue)
                filter &= filterBuilder.Lte(x => x.ReadingTime, endDate.Value);

            var info = await _collection.Find(filter).ToListAsync();
            return info.GroupBy(x => x.PanelId).ToDictionary(x => x.Key, x => x.ToList());
        }
    }
}
