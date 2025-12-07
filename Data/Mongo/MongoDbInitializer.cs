using Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Mongo
{
    public class MongoDbInitializer
    {
        private readonly IMongoDatabase _database;
        private readonly ILogger<MongoDbInitializer> _logger;
        public MongoDbInitializer(IMongoClient client, ILogger<MongoDbInitializer> logger)
        {
            _database = client.GetDatabase("MyDatabase");
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await CreatePanelReadingsCollectionAsync();
        }

        private async Task CreatePanelReadingsCollectionAsync()
        {
            var collectionName = "PanelReadings";

            // cria a collection caso não exista
            if (await CollectionExists(collectionName))
                return;
            
            await _database.CreateCollectionAsync(collectionName);

            var collection = _database.GetCollection<PanelReading>(collectionName);

            // índices
            var indexKeys = Builders<PanelReading>.IndexKeys
                .Ascending(x => x.PanelId)
                .Descending(x => x.ReadingTime);

            var indexModel = new CreateIndexModel<PanelReading>(indexKeys);

            var result = await collection.Indexes.CreateOneAsync(indexModel);
            _logger.LogInformation("Created index {Index} for collection {Collection}", result, collectionName);
        }


        private async Task<bool> CollectionExists(string name)
        {
            var filter = new BsonDocument("name", name);
            var collections = await _database.ListCollectionsAsync(
                new ListCollectionsOptions { Filter = filter }
            );

            return await collections.AnyAsync();
        }
    }
}