using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Data;
using VeloTimer.AmmcLoad.Models;

namespace VeloTimer.AmmcLoad.Services
{
    public class AmmcPassingService
    {
        private readonly IMongoCollection<Passing> _passings;
        private readonly ILogger<AmmcPassingService> _logger;

        private const long BAD_LOOP_ID = uint.MaxValue;

        public AmmcPassingService(IPassingDatabaseSettings settings, ILogger<AmmcPassingService> logger)
        {
            _logger = logger;
            _logger.LogInformation("creating");

            var client = new MongoClient(settings.ConnectionString);
            _logger.LogInformation("client ready");
            var database = client.GetDatabase(settings.PassingDatabase);
            _logger.LogInformation("database ready");

            _passings = database.GetCollection<Passing>(settings.PassingCollection);
            _logger.LogInformation("collection ready");

            _logger.LogInformation("created");
        }

        public async Task<List<Passing>> GetAll()
        {
            var builder = Builders<Passing>.Filter;
            var filter = builder.Ne(p => p.LoopId, BAD_LOOP_ID);
            var passings = await _passings.FindAsync<Passing>(filter);

            return await passings.ToListAsync();
        }

        public async Task<List<Passing>> GetAfterEntry(string id)
        {
            if (id == null)
            {
                return await GetAll();
            }

            var builder = Builders<Passing>.Filter;
            var filter = builder.Ne(p => p.LoopId, BAD_LOOP_ID) & builder.Gt(p => p.Id, id);
            var passings = _passings.Find<Passing>(filter).Limit(100);

            return await passings.ToListAsync();
        }

        public async Task<Passing> Get(string id)
        {
            var passing = await _passings.FindAsync(passing => passing.Id == id);
            return await passing.FirstOrDefaultAsync();
        }
    }
}
