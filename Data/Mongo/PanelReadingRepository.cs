using System.Globalization;
using Shared.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Data.Mongo
{
    public class PanelReadingRepository : IPanelReadingRepository
    {
        private readonly IMongoCollection<PanelReading> _collection;
        private readonly IMongoCollection<FlowConsumption> _flowConsumptionCollection;

        public PanelReadingRepository(IMongoClient client)
        {
            var db = client.GetDatabase("MyDb");
            _collection = db.GetCollection<PanelReading>("PanelReadings");
            _flowConsumptionCollection = db.GetCollection<FlowConsumption>("FlowConsumptions");
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

        public async Task<Dictionary<int, List<FlowConsumption>>> GetFlowConsumptionsOfPanels(IEnumerable<int> panelIds)
        {
            var panelIdList = panelIds.ToList();
            if (panelIdList.Count == 0)
                return new Dictionary<int, List<FlowConsumption>>();

            var now = DateTime.UtcNow;
            var lastHourStart = now.AddHours(-1);
            var startDate = now.AddMonths(-2);
            var todayStr = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // Beginning of current week (Sunday 00:00:00 UTC)
            var utcDate = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
            var currentWeekStart = utcDate.AddDays(-(int)now.DayOfWeek);
            // Previous calendar month (e.g. January when now is February)
            var lastMonth = now.AddMonths(-1).Month;
            var lastMonthYear = now.AddMonths(-1).Year;
            var groupDoc = new BsonDocument
            {
                { "_id", "$PanelId" },
                { "LastUpdated", new BsonDocument("$max", "$ReadingTime") },
                { "DayConsumption", new BsonDocument("$sum",
                    new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray
                        {
                            new BsonDocument("$dateToString", new BsonDocument { { "date", "$ReadingTime" }, { "format", "%Y-%m-%d" } }),
                            todayStr
                        }),
                        "$Value",
                        0
                    })) },
                { "WeekConsumption", new BsonDocument("$sum",
                    new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$gte", new BsonArray { "$ReadingTime", currentWeekStart }),
                            new BsonDocument("$lte", new BsonArray { "$ReadingTime", now })
                        }),
                        "$Value",
                        0
                    })) },
                { "MonthConsumption", new BsonDocument("$sum",
                    new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { new BsonDocument("$month", "$ReadingTime"), now.Month }),
                            new BsonDocument("$eq", new BsonArray { new BsonDocument("$year", "$ReadingTime"), now.Year })
                        }),
                        "$Value",
                        0
                    })) },
                { "LastMonthConsumption", new BsonDocument("$sum",
                    new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { new BsonDocument("$month", "$ReadingTime"), lastMonth }),
                            new BsonDocument("$eq", new BsonArray { new BsonDocument("$year", "$ReadingTime"), lastMonthYear })
                        }),
                        "$Value",
                        0
                    })) },
                { "ReadingsLastHour", new BsonDocument("$push", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$and", new BsonArray
                    {
                        new BsonDocument("$gte", new BsonArray { "$ReadingTime", lastHourStart }),
                        new BsonDocument("$lte", new BsonArray { "$ReadingTime", now })
                    }),
                    new BsonDocument { { "_id", "$_id" }, { "PanelId", "$PanelId" }, { "ReadingTime", "$ReadingTime" }, { "Value", "$Value" } },
                    new BsonString("$$REMOVE")
                })) }
            };


            var matchFilter = new BsonDocument
            {
                { "PanelId", new BsonDocument("$in", new BsonArray(panelIdList)) },
                { "ReadingTime", new BsonDocument("$gte", startDate) }
            };

            var pipeline = new[]
            {
                new BsonDocument("$match", matchFilter),
                new BsonDocument("$group", groupDoc)
            };

            var results = await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();

            var consumptions = new Dictionary<int, List<FlowConsumption>>();
            foreach (var doc in results)
            {
                var panelId = doc["_id"].AsInt32;
                var lastUpdated = doc.Contains("LastUpdated") && doc["LastUpdated"].BsonType == BsonType.DateTime
                    ? doc["LastUpdated"].AsBsonDateTime.ToUniversalTime()
                    : DateTime.MinValue;
                var readingsLastHour = GetReadingsLastHourFromDoc(doc);
                consumptions[panelId] = new List<FlowConsumption>
                {
                    new FlowConsumption
                    {
                        PanelId = panelId,
                        LastUpdated = lastUpdated,
                        DayConsumption = GetDouble(doc, "DayConsumption"),
                        WeekConsumption = GetDouble(doc, "WeekConsumption"),
                        MonthConsumption = GetDouble(doc, "MonthConsumption"),
                        LastMonthConsumption = Math.Max(0, GetDouble(doc, "LastMonthConsumption")),
                        ReadingsLastHour = readingsLastHour
                    }
                };
            }

            foreach (var panelId in panelIdList.Where(id => !consumptions.ContainsKey(id)))
                consumptions[panelId] = new List<FlowConsumption>();

            return consumptions;
        }

        private static (int year, int week) GetIsoWeek(DateTime date)
        {
            var calendar = CultureInfo.InvariantCulture.Calendar;
            var rule = CalendarWeekRule.FirstFourDayWeek;
            var week = calendar.GetWeekOfYear(date, rule, DayOfWeek.Monday);
            var year = date.Year;
            if (week >= 52 && date.Month == 1)
                year--;
            else if (week == 1 && date.Month == 12)
                year++;
            return (year, week);
        }

        private static double GetDouble(BsonDocument doc, string key)
        {
            if (!doc.Contains(key)) return 0.0;
            var v = doc[key];
            if (v.BsonType == BsonType.Int32) return v.AsInt32;
            if (v.BsonType == BsonType.Int64) return v.AsInt64;
            if (v.BsonType == BsonType.Double) return v.AsDouble;
            if (v.BsonType == BsonType.Decimal128) return (double)v.AsDecimal128;
            return 0.0;
        }

        private static List<PanelReading> GetReadingsLastHourFromDoc(BsonDocument doc)
        {
            if (!doc.Contains("ReadingsLastHour") || doc["ReadingsLastHour"].BsonType != BsonType.Array)
                return new List<PanelReading>();
            var list = new List<PanelReading>();
            foreach (var item in doc["ReadingsLastHour"].AsBsonArray)
            {
                if (item.IsBsonDocument)
                    list.Add(BsonSerializer.Deserialize<PanelReading>(item.AsBsonDocument));
            }
            return list;
        }
    }
}
