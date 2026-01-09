using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// 1. Servisleri Yapılandır
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB dosya sınırı
});

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => 
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// --- BELLEK TABANLI VERİ DEPOLAMA (SaaS MİMARİSİ) ---
// Kullanıcılar: [KullanıcıAdı -> Şifre]
var Users = new ConcurrentDictionary<string, string>();
// Odalar: [OdaAdı -> AdminConnectionId]
var RoomAdmins = new ConcurrentDictionary<string, string>();
// Mesaj Geçmişi: [OdaAdı -> Mesaj Listesi]
var RoomHistory = new ConcurrentDictionary<string, List<ChatMessage>>();

// --- API ENDPOINTLERİ (Kayıt, Giriş, Oda Listesi) ---

app.MapPost("/register", (UserDto dto) => 
    Users.TryAdd(dto.Username, dto.Password) ? Results.Ok() : Results.BadRequest("Bu kullanıcı zaten var."));

app.MapPost("/login", (UserDto dto) => 
    Users.TryGetValue(dto.Username, out var p) && p == dto.Password ? Results.Ok() : Results.Unauthorized());

app.MapGet("/list-rooms", () => RoomHistory.Keys.ToList());

app.MapGet("/", () => "SERVER v6.0");

// --- SIGNALR HUB (CANLI İLETİŞİM MERKEZİ) ---

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub 
{
    private static readonly ConcurrentDictionary<string, string> _admins = new();

    // 1. Odaya Katılma ve Geçmişi Yükleme
    public async Task JoinRoom(string roomName, string userName) 
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı ilk kuran kişiyi Admin yap
        _admins.TryAdd(roomName, Context.ConnectionId);

        // Sisteme odayı kaydet (Lobi listesi için)
        ChatData.AddRoomIfEmpty(roomName);

        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"🚀 {userName} odaya iniş yaptı.");
        
        if (_admins[roomName] == Context.ConnectionId)
            await Clients.Caller.SendAsync("ReceiveSystemMessage", "👑 Tebrikler, odanın kontrolü sizde (ADMİN).");

        // Varsa geçmiş mesajları gönder
        if (ChatData.History.TryGetValue(roomName, out var history)) {
            foreach (var msg in history) {
                await Clients.Caller.SendAsync("ReceiveMessage", msg.User, msg.Msg, msg.Iv, msg.IsFile, msg.Time);
            }
        }
    }

    // 2. Mesaj Gönderimi ve Kaydı
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
    {
        var chatMsg = new ChatMessage(user, msg, iv, isFile, DateTime.UtcNow);
        
        // Geçmişi kaydet
        ChatData.SaveMessage(room, chatMsg);

        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, chatMsg.Time);
    }

    // 3. "Görüldü" Bilgisini Dağıt
    public async Task SendSeen(string room, string user) 
    {
        await Clients.OthersInGroup(room).SendAsync("ReceiveSeen", user);
    }

    // 4. "Yazıyor..." Bilgisini Dağıt
    public async Task SendTyping(string room, string user) 
    {
        await Clients.OthersInGroup(room).SendAsync("ReceiveTyping", user);
    }

    // 5. Admin Yetkisi: Kullanıcıyı At (Kick)
    public async Task KickUser(string room, string targetUser) 
    {
        if (_admins.TryGetValue(room, out var adminId) && Context.ConnectionId == adminId) {
            await Clients.Group(room).SendAsync("UserKicked", targetUser);
        }
    }
}

// --- VERİ MODELLERİ ---
public record UserDto(string Username, string Password);
public record ChatMessage(string User, string Msg, string Iv, bool IsFile, DateTime Time);

// Geçmiş yönetimi için yardımcı sınıf
public static class ChatData {
    public static ConcurrentDictionary<string, List<ChatMessage>> History = new();
    
    public static void AddRoomIfEmpty(string room) {
        if (!History.ContainsKey(room)) History[room] = new List<ChatMessage>();
    }

    public static void SaveMessage(string room, ChatMessage msg) {
        if (History.TryGetValue(room, out var list)) {
            list.Add(msg);
            if (list.Count > 100) list.RemoveAt(0); // Son 100 mesaj sınırı
        }
    }
}
