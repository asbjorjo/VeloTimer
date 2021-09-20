using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Server.Models.Mylaps;

namespace VeloTimerWeb.Server.Services.Mylaps
{
    public class AmmcPassingService
    {
        private readonly IMongoCollection<Passing> _passings;

        public AmmcPassingService(IPassingDatabaseSettings settings)
        {
            var client = new MongoClient(settings.ConnectionString);
            var database = client.GetDatabase(settings.PassingDatabase);

            _passings = database.GetCollection<Passing>(settings.PassingCollection);
        }

        public async Task<List<Passing>> GetAll()
        {
            var passings = await _passings.FindAsync(passing => passing.LoopId < int.MaxValue);
            return await passings.ToListAsync();
        }

        public async Task<List<Passing>> GetAfterTime(DateTime Time)
        {
            var passings = await _passings.FindAsync(passing => passing.LoopId < int.MaxValue
                                             && passing.UtcTime > Time.ToUniversalTime());
            return await passings.ToListAsync();
        }

        public async Task<Passing> Get(string id)
        {
            var passing = await _passings.FindAsync(passing => passing.Id == id);
            return await passing.FirstOrDefaultAsync();
        }
    }
}
