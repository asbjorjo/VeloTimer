using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Hub;
using System.Collections.Generic;
using System.Linq;

namespace VeloTimerWeb.Server.Hubs
{
    public class PassingHub : Hub<IPassingClient>
    {
        public async Task NotifySegmentOfNewRun(SegmentRun segment)
        {
            await Clients.Groups($"segment_{segment.SegmentId}").NewSegmentRun();
        }

        public async Task RegisterPassingWithClients(Passing passing)
        {
            await Clients.All.RegisterPassing(passing);
        }

        public async Task RegisterLoopPassing(Passing passing)
        {
            await Clients.Group(passing.LoopId.ToString()).RegisterPassing(passing);
        }

        public async Task SendLastPassingToClients(Passing passing)
        {
            await Clients.All.LastPassing(passing);
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

        public Task RemoveSegmentLoopGroup(long segment)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"segment_{segment}");
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
