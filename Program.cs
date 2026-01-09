using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// Bellek üzerindeki verileri standart Thread-Safe yapılarla tutuyoruz
var users = new ConcurrentDictionary<string, string>(); // Kullanıcı adı, Şifre
var rooms = new ConcurrentDictionary<string, bool>(); // Oda adı, Şifreli mi?
var roomAdmins = new ConcurrentDictionary<string, string>(); // Oda adı, Admin adı
var mutedUsers = new ConcurrentDictionary<string, byte>(); // Susturulanlar

app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub
{
    // Static referanslar (Sınıfın her örneğinde aynı kalması için)
    private static ConcurrentDictionary<string, string> RoomAdmins = new();
    private static ConcurrentDictionary<string, byte> MutedUsers = new();

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı ilk kuran admin olur
        RoomAdmins.TryAdd(roomName, userName);

        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        if (MutedUsers.ContainsKey(user)) return; // Mute kontrolü
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target)
    {
        // Admin yetki kontrolü eklenebilir
        switch (action.ToUpper()) 
        {
            case "KICK": 
                await Clients.All.SendAsync("ReceiveAdminAction", "KICK", target); 
                break;
            case "MUTE": 
                MutedUsers.TryAdd(target, 0); 
                await Clients.All.SendAsync("ReceiveAdminAction", "MUTE", target); 
                break;
            case "UNMUTE": 
                MutedUsers.TryRemove(target, out _); 
                await Clients.All.SendAsync("ReceiveAdminAction", "UNMUTE", target); 
                break;
        }
    }
}

public record User(string Username, string Password);
