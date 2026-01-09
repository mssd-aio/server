using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// VERİ DEPOLARI
var users = new ConcurrentDictionary<string, string>(); 
var rooms = new ConcurrentDictionary<string, bool>(); 
var bannedUsers = new ConcurrentHashSet<string>(); // Banlı listesi
var globalAdmins = new ConcurrentHashSet<string>(); // Root'un atadığı yetkililer

app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => {
    if (bannedUsers.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    return users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized();
});
app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");
app.Run();

public class ChatHub : Hub
{
    public static ConcurrentDictionary<string, string> UserRooms = new();
    public static ConcurrentHashSet<string> GlobalAdmins = new();
    public static ConcurrentHashSet<string> BannedList = new();

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        if (BannedList.Contains(userName)) { await Clients.Caller.SendAsync("ReceiveAdminAction", "BAN", userName); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        UserRooms[userName] = roomName;
        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target, string extra = "")
    {
        string caller = target; // Basitleştirilmiş, normalde auth üzerinden alınır
        bool isRoot = (extra == "kaytshine_token"); // Root doğrulaması

        switch (action.ToUpper())
        {
            case "BAN":
                if (isRoot) { BannedList.Add(target); await Clients.All.SendAsync("ReceiveAdminAction", "BAN", target); }
                break;
            case "ANNOUNCE":
                if (isRoot) await Clients.All.SendAsync("ReceiveMessage", "GLOBAL DUYURU", extra, "", false, DateTime.Now);
                break;
            case "PROMOTE": // Yetki Ver
                if (isRoot) GlobalAdmins.Add(target);
                break;
            case "DEMOTE": // Yetki Al
                if (isRoot) GlobalAdmins.Remove(target);
                break;
            case "LIST_ALL":
                if (isRoot) await Clients.Caller.SendAsync("ReceiveSuperAdminPanel", UserRooms.Select(x => $"{x.Key} ({x.Value})").ToList());
                break;
        }
    }
}

public class ConcurrentHashSet<T> : ConcurrentDictionary<T, byte> {
    public void Add(T item) => TryAdd(item, 0);
    public bool Contains(T item) => ContainsKey(item);
    public void Remove(T item) => TryRemove(item, out _);
}
public record User(string Username, string Password);
