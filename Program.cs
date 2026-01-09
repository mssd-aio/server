using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// 1. SERVİS YAPILANDIRMASI
builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// 2. CORS AYARLARI (İstemcinin bağlanabilmesi için)
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// 3. BELLEK ÜSTÜNDEKİ VERİ DEPOLARI (Thread-Safe)
// Kullanıcı Verileri: <Username, Password>
var users = new ConcurrentDictionary<string, string>(); 
// Oda Bilgileri: <RoomName, IsProtected (bool)>
var rooms = new ConcurrentDictionary<string, bool>(); 
// Aktif Bağlantılar: <ConnectionId, Username>
var activeConnections = new ConcurrentDictionary<string, string>();
// Oda Adminleri: <RoomName, AdminUsername>
var roomAdmins = new ConcurrentDictionary<string, string>();
// Global Mute Listesi: <Username, byte>
var globalMuted = new ConcurrentDictionary<string, byte>();

// 4. API ENDPOINTLERİ
app.MapPost("/register", (User u) => {
    if (string.IsNullOrEmpty(u.Username) || u.Username.Length < 3) return Results.BadRequest();
    return users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.Conflict();
});

app.MapPost("/login", (User u) => {
    if (users.TryGetValue(u.Username, out var pass) && pass == u.Password) return Results.Ok();
    return Results.Unauthorized();
});

app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

// 5. SIGNALR HUB (İLETİŞİM MERKEZİ)
app.MapHub<ChatHub>("/chatHub");

app.Run();

// --- HUB SINIFI ---
public class ChatHub : Hub
{
    // Hub içinde erişim için statik referanslar (Basitleştirilmiş)
    private static ConcurrentDictionary<string, string> RoomAdminsMap = new();
    private static ConcurrentDictionary<string, string> UserCurrentRoom = new();
    private static ConcurrentDictionary<string, byte> MutedUsers = new();

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Kullanıcıyı odaya kaydet
        UserCurrentRoom[userName] = roomName;

        // Odayı ilk kuran admin olur (Eğer oda listede yoksa)
        // Static bir listeye oda ismini ve şifre durumunu ekle (Global erişim için)
        // Not: Gerçek projede 'rooms' ConcurrentDictionary'sine erişim gerekebilir.

        if (!RoomAdminsMap.ContainsKey(roomName))
        {
            RoomAdminsMap[roomName] = userName;
        }

        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} odaya katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Mute Kontrolü
        if (MutedUsers.ContainsKey(user))
        {
            await Clients.Caller.SendAsync("ReceiveAdminAction", "MUTE_NOTIFY", user);
            return;
        }

        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target)
    {
        // Komutu gönderen kim? (Username tespiti için basit mantık - Client'tan gelen 'user' parametresi de eklenebilir)
        // Burada basitlik adına target ve action üzerinden işlem yapıyoruz.
        // 'kaytshine' kontrolü client tarafında yapıldığı gibi burada da yapılabilir.

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

            case "WHOIS":
                var targetRoom = UserCurrentRoom.GetValueOrDefault(target, "Bilinmiyor");
                var isAdm = RoomAdminsMap.Values.Contains(target);
                await Clients.Caller.SendAsync("ReceiveWhois", target, targetRoom, isAdm);
                break;

            case "PANEL": // Standart Admin Paneli
                var onlineUsers = UserCurrentRoom.Select(x => $"{x.Key} ({x.Value})").ToList();
                await Clients.Caller.SendAsync("ReceiveAdminPanel", onlineUsers);
                break;

            case "LIST_ALL_USERS": // ROOT (kaytshine) Paneli
                var allData = UserCurrentRoom.Select(x => $"[ROOT] {x.Key} -> Oda: {x.Value}").ToList();
                await Clients.Caller.SendAsync("ReceiveSuperAdminPanel", allData);
                break;

            case "TAKE_LEAD": // kaytshine odaya el koyar
                RoomAdminsMap[room] = "kaytshine";
                await Clients.Group(room).SendAsync("ReceiveMessage", "SYSTEM", "DİKKAT: Oda yönetimi ROOT (kaytshine) tarafından devralındı.", "", false, DateTime.Now);
                break;
        }
    }

    // Bağlantı kesildiğinde kullanıcıyı listeden düş
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // ConnectionId üzerinden username bulunup temizlik yapılabilir.
        await base.OnDisconnectedAsync(exception);
    }
}

// 6. MODELLER
public record User(string Username, string Password);
