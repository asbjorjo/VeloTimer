using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimerConsole.Data;
using VeloTimerConsole.Models;

namespace VeloTimerConsole.Services
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
