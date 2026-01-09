using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; 
});

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- VERİTABANI (BELLEKTE) ---
var users = new ConcurrentDictionary<string, string>(); 
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

// BURASI DÜZELTİLDİ: Artık ChatStore içindeki odaları döndürüyor
app.MapGet("/list-rooms", () => {
    return ChatStore.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value });
});

app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- CHAT HUB MANTIĞI ---
public class ChatHub : Hub
{
    // Client 3 parametre gönderiyor: (roomName, userName, isProtected)
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odaları ortak Store'a ekle ki /list-rooms görebilsin
        ChatStore.Rooms[roomName] = isProtected;

        Console.WriteLine($"[LOG] {userName} odaya katildi: {roomName} (Sifreli mi: {isProtected})");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Şifreli veriyi gruptakilere dağıt
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }
}

// Global Depo
public static class ChatStore {
    // Odaların tutulduğu asıl yer burası
    public static ConcurrentDictionary<string, bool> Rooms = new();
}

public record User(string Username, string Password);
public record RoomMeta(string Name, bool IsProtected);
