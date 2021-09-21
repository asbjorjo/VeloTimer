using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Server.Hubs
{
    public class PassingHub : Hub
    {
        public const string hubUrl = "/hub/passing";

        public async Task RegisterPassing(Passing passing)
        {
            await Clients.All.SendAsync("NewPassing", passing);
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
