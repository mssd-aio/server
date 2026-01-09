using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// SignalR Ayarları (10MB Limit)
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; 
});

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- BELLEKTEKİ VERİLER ---
var users = new ConcurrentDictionary<string, string>(); // Kullanıcı:Şifre
var bannedUsers = new ConcurrentBag<string>();

// --- HTTP ENDPOINTLERİ ---

app.MapPost("/register", (User u) => {
    if (string.IsNullOrEmpty(u.Username) || u.Username.Length < 3) return Results.BadRequest();
    return users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest();
});

app.MapPost("/login", (User u) => {
    if (bannedUsers.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    if (users.TryGetValue(u.Username, out var pass) && pass == u.Password) return Results.Ok();
    return Results.Unauthorized();
});

// Oda Listeleme (ChatStore üzerinden)
app.MapGet("/list-rooms", () => ChatStore.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- CHAT HUB MANTIĞI ---
public class ChatHub : Hub
{
    // Odaya Katılma
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatStore.Rooms[roomName] = isProtected;
        Console.WriteLine($"[LOG] {userName} odaya katildi: {roomName}");
    }

    // Mesaj Gönderme
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Mesajı odadaki herkese (gönderen dahil) zaman damgasıyla yayar
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    // --- YENİ: GÖRÜLDÜ SİSTEMİ ---
    public async Task MessageSeen(string room, string user)
    {
        // Bu metod çağrıldığında o odadaki herkese "user" isimli kişinin 
        // mesajları okuduğu bilgisini gönderir.
        await Clients.Group(room).SendAsync("UserSeen", user);
    }
}

// Yardımcı Depo
public static class ChatStore {
    public static ConcurrentDictionary<string, bool> Rooms = new();
}

public record User(string Username, string Password);
