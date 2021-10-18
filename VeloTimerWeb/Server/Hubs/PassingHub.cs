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

        public async Task NotifyLoopsOfNewPassings(List<long> loops)
        {
            await Clients.Groups(loops.Select(l => l.ToString()).ToList()).NewPassings();
        }

        public async Task AddToTimingLoopGroup(long loop)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, loop.ToString());
        }

        public async Task RemoveFromTimingLoopGroup(long loop) 
        { 
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, loop.ToString()); 
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
