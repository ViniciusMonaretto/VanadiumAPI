using Shared.Models;
using MongoDB.Driver;

namespace Data.Mongo
{
    public class PanelReadingRepository: IPanelReadingRepository
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
                IsOrdered = false // Continue inserting even if some documents fail
            };
            
            try
            {
                await _collection.InsertManyAsync(panelReadings, options);
            }
            catch (MongoBulkWriteException<PanelReading> ex)
            {
                // Log duplicate key errors but don't fail completely
                // Other documents may have been inserted successfully
                var duplicateErrors = ex.WriteErrors
                    .Where(e => e.Category == ServerErrorCategory.DuplicateKey)
                    .ToList();
                
                if (duplicateErrors.Count < ex.WriteErrors.Count)
                {
                    // Some documents were inserted successfully, so partial success
                    throw;
                }
                // If all errors are duplicates, we can ignore them
                // (this means the data was already inserted)
            }
        }

        public async Task<IEnumerable<PanelReading>> GetPanelReadingsByPanelId(int panelId, DateTime? startDate, DateTime? endDate)
        {
            return await _collection.Find(x => x.PanelId == panelId && 
                                        x.ReadingTime >= startDate && 
                                        x.ReadingTime <= endDate)
            .ToListAsync();
        }

        public async Task<Dictionary<int, List<PanelReading>>> GetPanelReadingsByPanelIds(IEnumerable<int> panelIds, DateTime? startDate, DateTime? endDate)
        {
            var info = await _collection.Find(x => panelIds.Contains(x.PanelId) && 
                                        x.ReadingTime >= startDate && 
                                        x.ReadingTime <= endDate).ToListAsync();

            return info.GroupBy(x => x.PanelId).ToDictionary(x => x.Key, x => x.ToList());
        }
    }
}