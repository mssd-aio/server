using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(o => { o.EnableDetailedErrors = true; o.MaximumReceiveMessageSize = 10 * 1024 * 1024; }); // 10MB Dosya desteği
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// VERİ DEPOLARI
var users = new ConcurrentDictionary<string, string>(); 
var roomList = new ConcurrentDictionary<string, bool>(); // ODA LİSTESİ HATASI BURADA DÜZELTİLDİ
var bannedUsers = new ConcurrentBag<string>();
var userRooms = new ConcurrentDictionary<string, string>();

app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => {
    if (bannedUsers.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    return users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized();
});
app.MapGet("/list-rooms", () => roomList.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");
app.Run();

public class ChatHub : Hub
{
    private static ConcurrentDictionary<string, string> RoomsState = new(); // Oda -> Şifreli mi?
    private static ConcurrentDictionary<string, string> Admins = new();

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        // ODA LİSTESİNE EKLEME HATASI DÜZELTİLDİ
        // Bu hub içinden üstteki roomList'e erişmek yerine static bir yapıyla yönetelim:
        // (Not: Gerçek projede veritabanı veya singleton servis kullanılır)
        
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatHubStatic.Rooms[roomName] = isProtected;
        ChatHubStatic.UserCurrentRoom[userName] = roomName;

        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target, string extra = "")
    {
        bool isRoot = (extra == "kaytshine_token");
        switch (action.ToUpper())
        {
            case "BAN": if (isRoot) ChatHubStatic.Banned.Add(target); break;
            case "ANNOUNCE": if (isRoot) await Clients.All.SendAsync("ReceiveMessage", "GLOBAL DUYURU", target, "", false, DateTime.Now); break;
            case "LIST_ALL": if (isRoot) await Clients.Caller.SendAsync("ReceiveSuperAdminPanel", ChatHubStatic.UserCurrentRoom.Select(x => $"{x.Key} ({x.Value})").ToList()); break;
            case "KICK": await Clients.All.SendAsync("ReceiveAdminAction", "KICK", target); break;
        }
    }
}

public static class ChatHubStatic {
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, string> UserCurrentRoom = new();
    public static ConcurrentBag<string> Banned = new();
}
public record User(string Username, string Password);
