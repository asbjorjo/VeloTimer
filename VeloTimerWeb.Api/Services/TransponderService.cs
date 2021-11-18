using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Services
{
    public class TransponderService : ITransponderService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<TransponderService> _logger;

        public TransponderService(VeloTimerDbContext context, ILogger<TransponderService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            totime = totime ?? DateTimeOffset.MaxValue;

            var active = await _context.Set<Passing>()
                .Where(p => p.Time >= fromtime && p.Time < totime)
                .Select(p => p.TransponderId)
                .Distinct()
                .CountAsync();

            return active;
        }
    }
}
