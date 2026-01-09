using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// SignalR ve Dosya Boyutu Ayarları (15MB)
builder.Services.AddSignalR(options => {
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024; 
});

builder.Services.AddCors();

var app = builder.Build();

// Client bağlantısı için CORS izni
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- BELLEKTEKİ VERİLER ---
var users = new ConcurrentDictionary<string, string>(); // Kullanıcı:Şifre
var rooms = new ConcurrentDictionary<string, bool>();   // Oda:ŞifreliMi

// --- API ENDPOINTLERİ ---

app.MapPost("/register", (User u) => {
    if (string.IsNullOrEmpty(u.Username)) return Results.BadRequest();
    return users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.Conflict();
});

app.MapPost("/login", (User u) => {
    if (users.TryGetValue(u.Username, out var pass) && pass == u.Password) return Results.Ok();
    return Results.Unauthorized();
});

app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

// --- SIGNALR HUB (Client'daki Invoke'lara Birebir Uygun) ---
app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public class ChatHub : Hub
{
    // Client: hub.InvokeAsync("JoinRoom", currentRoom, currentUser, isProtected)
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatStore.Rooms.TryAdd(roomName, isProtected);
    }

    // Client: hub.InvokeAsync("SendMessage", currentRoom, currentUser, encMsg, iv, isFile)
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Client: hub.On("ReceiveMessage", (u, m, iv, isFile, t) => { ... })
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }
}

public static class ChatStore {
    public static ConcurrentDictionary<string, bool> Rooms = new();
}

public record User(string Username, string Password);
