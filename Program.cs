using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// SignalR ve CORS Ayarları
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024; // 15MB Dosya sınırı
});

builder.Services.AddCors();

var app = builder.Build();

// Client'ın bağlanabilmesi için CORS'u açıyoruz
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- BELLEKTEKİ VERİLER (Basit Veritabanı) ---
var users = new ConcurrentDictionary<string, string>(); // Username, Password
var rooms = new ConcurrentDictionary<string, bool>();   // RoomName, IsProtected

// --- HTTP ENDPOINTLERİ ---

// Kayıt Ol
app.MapPost("/register", (User u) => {
    if (string.IsNullOrEmpty(u.Username) || u.Username.Length < 3) return Results.BadRequest();
    return users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.Conflict();
});

// Giriş Yap
app.MapPost("/login", (User u) => {
    if (users.TryGetValue(u.Username, out var pass) && pass == u.Password) return Results.Ok();
    return Results.Unauthorized();
});

// Odaları Listele
app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

// Hub Map
app.MapHub<ChatHub>("/chatHub");

app.Run();

// --- SIGNALR HUB MANTIĞI ---
public class ChatHub : Hub
{
    // Odaya Katılma
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatStore.Rooms.TryAdd(roomName, isProtected);
        Console.WriteLine($"[LOG] {userName} odaya girdi: {roomName}");
    }

    // Mesaj ve Dosya Gönderme
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Odaya bağlı herkese mesajı ilet (Zaman damgası sunucudan eklenir)
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    // Görüldü Bilgisini İletme
    public async Task MessageSeen(string room, string user)
    {
        // Bu sinyal geldiğinde, o odadaki diğer kişilere bu kullanıcının mesajları gördüğünü bildirir
        await Clients.Group(room).SendAsync("UserSeen", user);
    }
}

// Global Oda Deposu
public static class ChatStore {
    public static ConcurrentDictionary<string, bool> Rooms = new();
}

public record User(string Username, string Password);
public record RoomMeta(string Name, bool IsProtected);
