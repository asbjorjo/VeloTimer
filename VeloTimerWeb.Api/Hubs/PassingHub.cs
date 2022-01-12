using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Hub;
using VeloTimerWeb.Api.Models.Timing;

namespace VeloTimerWeb.Api.Hubs
{
    public class PassingHub : Hub<IPassingClient>
    {
        //public async Task NotifySegmentOfNewRun(SegmentRun segment)
        //{
        //    await Clients.Groups($"segment_{segment.Segment.Id}").NewSegmentRun(segment);
        //}

        //public async Task NotifyTransponderOfNewRun(Transponder transponder, SegmentRun segment)
        //{
        //    await Clients.Groups($"segment_{segment.Segment.Id}_transponder_{transponder.Id}").NewSegmentRun(segment, transponder);
        //}

        public async Task RegisterPassingWithClients(Passing passing)
        {
            //await Clients.All.RegisterPassing(passing.ToWeb());
        }

        public async Task RegisterLoopPassing(Passing passing)
        {
            //await Clients.Group(passing.Loop.Id.ToString()).RegisterPassing(passing.ToWeb());
        }

        public async Task SendLastPassingToClients(Passing passing)
        {
            //await Clients.All.LastPassing(passing.ToWeb());
        }

        public async Task NotifyClientsOfNewPassings()
        {
            await Clients.All.NewPassings();
        }

        public async Task NotifyLoopOfNewPassing(long loop)
        {
            await Clients.Groups($"loop_{loop}").NewPassings();
        }

        public async Task NotifyLoopsOfNewPassings(List<long> loops)
        {
            await Clients.Groups(loops.Select(l => $"loop_{l}").ToList()).NewPassings();
        }

        public Task AddToTimingLoopGroup(long loop)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, $"loop_{loop}");
        }

        public Task RemoveFromTimingLoopGroup(long loop)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"loop_{loop}");
        }

        public Task AddToSegmentGroup(long segment)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, $"segment_{segment}");
        }

        public Task RemoveFromSegmentGroup(long segment)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"segment_{segment}");
        }

        public Task AddToSegmentTransponderGroup(long segment, long transponder)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, $"segment_{segment}_transponder_{transponder}");
        }

        public Task RemoveFromSegmentTransponderGroup(long segment, long transponder)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"segment_{segment}_transponder_{transponder}");
        }

        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"{Context.ConnectionId} connected");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"{Context.ConnectionId} disconnected");
            return base.OnDisconnectedAsync(exception);
        }
    }
}
