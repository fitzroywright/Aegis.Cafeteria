namespace Aegis.Cafeteria.Services.Realtime;

using Microsoft.AspNetCore.SignalR;

public sealed class ManagementEventsHub : Hub
{
    public const string EventsAvailableMethod = "EventsAvailable";
}
