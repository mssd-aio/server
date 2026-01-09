using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => 
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// --- API ENDPOINTLERİ ---

// Kayıt ve Giriş (Basit Versiyon)
app.MapPost("/register", (UserDto dto) => Results.Ok());
app.MapPost("/login", (UserDto dto) => Results.Ok());

// LOBİ LİSTESİ: Burası ChatManager'dan besleniyor
app.MapGet("/list-rooms", () => 
    ChatManager.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapGet("/", () => "🛡️ SECURE SERVER v8.7 - ONLINE");

app.MapHub<ChatHub>("/chatHub");

app.Run();

// --- VERİ YÖNETİMİ ---
// Hata almamak için değişkenleri bu statik sınıfa taşıdık
public static class ChatManager 
{
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, string> ConnectionToRoom = new();
    public static ConcurrentDictionary<string, string> ConnectionToUser = new();
}

// --- SIGNALR HUB ---
public class ChatHub : Hub 
{
    // ÖNEMLİ: Client 3 parametre gönderiyor (Oda, Kullanıcı, ŞifreliMi)
    public async Task JoinRoom(string roomName, string userName, bool isProtected) 
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Lobi listesini güncelle
        ChatManager.Rooms.TryAdd(roomName, isProtected);
        ChatManager.ConnectionToRoom[Context.ConnectionId] = roomName;
        ChatManager.ConnectionToUser[Context.ConnectionId] = userName;

        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"🚀 {userName} odaya katıldı.");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
    {
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.UtcNow);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ChatManager.ConnectionToRoom.TryRemove(Context.ConnectionId, out var room) && 
            ChatManager.ConnectionToUser.TryRemove(Context.ConnectionId, out var user))
        {
            await Clients.Group(room).SendAsync("ReceiveSystemMessage", $"🚪 {user} odadan ayrıldı.");
        }
        await base.OnDisconnectedAsync(exception);
    }
}

public record UserDto(string Username, string Password);
