using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// SignalR ve Dosya Boyutu Ayarları (10MB limit)
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; 
});

builder.Services.AddCors();

var app = builder.Build();

// CORS Ayarı: Client'ın sunucuya erişebilmesi için şart
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- VERİTABANI (BELLEKTE) ---
var users = new ConcurrentDictionary<string, string>(); // Username, Password
var rooms = new ConcurrentDictionary<string, bool>();   // RoomName, IsProtected
var bannedUsers = new ConcurrentBag<string>();

// --- HTTP ENDPOINTLERİ ---

// Kayıt Olma
app.MapPost("/register", (User u) => {
    if (string.IsNullOrEmpty(u.Username) || u.Username.Length < 3) return Results.BadRequest("Gecersiz kullanıcı adı.");
    return users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest("Bu kullanıcı adı zaten alınmış.");
});

// Giriş Yapma
app.MapPost("/login", (User u) => {
    if (bannedUsers.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    if (users.TryGetValue(u.Username, out var pass) && pass == u.Password) return Results.Ok();
    return Results.Unauthorized();
});

// Odaları Listeleme
app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

// SignalR Hub Bağlantısı
app.MapHub<ChatHub>("/chatHub");

// Render/Heroku gibi platformlar için Port ayarı
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- CHAT HUB MANTIĞI ---
public class ChatHub : Hub
{
    // Odaya Giriş
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı sistem listesine ekle
        ChatStore.Rooms[roomName] = isProtected;

        // Odaya katılım mesajı (Client'ın şifreleme mantığına uygun boş IV ile gönderim)
        // Not: Client'ın şifreleme anahtarı oda şifresine bağlı olduğu için 
        // sistem mesajları bazen client tarafında 'catch' bloğuna düşebilir.
    }

    // Mesaj ve Dosya Dağıtımı
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Gelen şifreli paketi odadaki herkese (gönderen dahil) geri yollar.
        // Client tarafı bu paketi alıp kendi anahtarıyla çözecek.
        // DM (/msg) komutları da bu kanal üzerinden şifreli akar ve client'ın kendi içinde ayrıştırılır.
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }
}

// Yardımcı Sınıf
public static class ChatStore {
    public static ConcurrentDictionary<string, bool> Rooms = new();
}

public record User(string Username, string Password);
