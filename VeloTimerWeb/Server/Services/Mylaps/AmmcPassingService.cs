using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Server.Models.Mylaps;

namespace VeloTimerWeb.Server.Services.Mylaps
{
    public class AmmcPassingService
    {
        private readonly IMongoCollection<Passing> _passings;
        private readonly ILogger<AmmcPassingService> _logger;

        private const long BAD_LOOP_ID = uint.MaxValue;

        public AmmcPassingService(IPassingDatabaseSettings settings, ILogger<AmmcPassingService> logger)
        {
            var client = new MongoClient(settings.ConnectionString);
            var database = client.GetDatabase(settings.PassingDatabase);

            _passings = database.GetCollection<Passing>(settings.PassingCollection);
            _logger = logger;
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
            var passings = await _passings.FindAsync<Passing>(filter);

            return await passings.ToListAsync();
        }

        public async Task<Passing> Get(string id)
        {
            var passing = await _passings.FindAsync(passing => passing.Id == id);
            return await passing.FirstOrDefaultAsync();
        }
    }
}
