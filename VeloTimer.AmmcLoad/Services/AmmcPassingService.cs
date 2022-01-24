using AmmcLoad.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Models;

namespace VeloTimer.AmmcLoad.Services
{
    public class AmmcPassingService
    {
        private readonly IMongoCollection<PassingAmmc> _passings;
        private readonly ILogger<AmmcPassingService> _logger;

        private const long BAD_LOOP_ID = uint.MaxValue;

        public AmmcPassingService(PassingDatabaseSettings options, ILogger<AmmcPassingService> logger)
        {
            _logger = logger;
            var settings = options;

            var client = new MongoClient(settings.ConnectionString);
            var database = client.GetDatabase(settings.PassingDatabase);

            _passings = database.GetCollection<PassingAmmc>(settings.PassingCollection);
        }

        public async Task<List<PassingAmmc>> GetAll()
        {
            var builder = Builders<PassingAmmc>.Filter;
            var filter = builder.Ne(p => p.LoopId, BAD_LOOP_ID);
            var passings = await _passings.FindAsync<PassingAmmc>(filter);

            return await passings.ToListAsync();
        }

        public async Task<List<PassingAmmc>> GetAfterEntry(string id)
        {
            if (id == null)
            {
                return await GetAll();
            }

            var builder = Builders<PassingAmmc>.Filter;
            var filter = builder.Ne(p => p.LoopId, BAD_LOOP_ID) & builder.Gt(p => p.Id, id);
            var passings = await _passings.FindAsync<PassingAmmc>(filter);

            return await passings.ToListAsync();
        }

        public async Task<PassingAmmc> Get(string id)
        {
            var passing = await _passings.FindAsync(passing => passing.Id == id);
            return await passing.FirstOrDefaultAsync();
        }
    }
}
