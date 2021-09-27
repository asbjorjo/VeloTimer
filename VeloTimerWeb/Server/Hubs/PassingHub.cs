using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Hub;

namespace VeloTimerWeb.Server.Hubs
{
    public class PassingHub : Hub<IPassingClient>
    {
        public async Task RegisterPassingWithClients(Passing passing)
        {
            await Clients.All.RegisterPassing(passing);
        }

        public async Task SendLastPassingToClients(Passing passing)
        {
            await Clients.All.LastPassing(passing);
        }

        public async Task NotifyClientsOfNewPassings()
        {
            await Clients.All.NewPassings();
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
