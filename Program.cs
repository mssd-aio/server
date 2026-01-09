using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// Bellek üzerindeki veri depoları
var users = new ConcurrentDictionary<string, string>(); // Username, Password
var rooms = new ConcurrentList<Room>();
var roomAdmins = new ConcurrentDictionary<string, string>(); // RoomName, AdminUsername
var mutedUsers = new ConcurrentHashSet<string>(); // Muted Usernames

app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => rooms);

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub
{
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı ilk kuran admin olur
        if (!ChatHubStatic.RoomAdmins.ContainsKey(roomName)) {
            ChatHubStatic.RoomAdmins[roomName] = userName;
        }

        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        if (ChatHubStatic.MutedUsers.Contains(user)) return; // Mute kontrolü
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target)
    {
        // Yetki Kontrolü: Komutu gönderen admin mi?
        // (Gerçek projede Context.User ile doğrulanmalı, burada basitleştirildi)
        
        switch (action.ToUpper()) {
            case "KICK": await Clients.All.SendAsync("ReceiveAdminAction", "KICK", target); break;
            case "MUTE": ChatHubStatic.MutedUsers.Add(target); await Clients.All.SendAsync("ReceiveAdminAction", "MUTE", target); break;
            case "UNMUTE": ChatHubStatic.MutedUsers.Remove(target); await Clients.All.SendAsync("ReceiveAdminAction", "UNMUTE", target); break;
        }
    }
}

// Static veri saklama alanı
public static class ChatHubStatic {
    public static ConcurrentDictionary<string, string> RoomAdmins = new();
    public static HashSet<string> MutedUsers = new();
}

public record User(string Username, string Password);
public record Room(string Name, bool IsProtected);
