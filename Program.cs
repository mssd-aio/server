using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Türkçe karakter desteği için Encoding ayarı
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => 
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// --- BELLEK VERİ MERKEZİ ---
// Kullanıcılar (KullanıcıAdı : Şifre)
var Users = new ConcurrentDictionary<string, string>();
// Aktif Odalar (OdaAdı : ŞifreliMi)
var GlobalRooms = new ConcurrentDictionary<string, bool>();

// --- API ENDPOINTLERİ ---

// Kayıt Ol
app.MapPost("/register", (UserDto dto) => 
    Users.TryAdd(dto.Username, dto.Password) ? Results.Ok() : Results.BadRequest());

// Giriş Yap
app.MapPost("/login", (UserDto dto) => 
    Users.TryGetValue(dto.Username, out var p) && p == dto.Password ? Results.Ok() : Results.Unauthorized());

// Lobi: Odaları Listele (İstemcinin beklediği RoomMeta formatında)
app.MapGet("/list-rooms", () => 
    GlobalRooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapGet("/", () => "🛡️ SECURE SERVER v8.5 [TR] - ONLINE");

// --- SIGNALR HUB ---

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub 
{
    // Bağlantı ID'lerini oda ve kullanıcı adlarıyla eşleştiriyoruz
    private static readonly ConcurrentDictionary<string, string> _connectionToRoom = new();
    private static readonly ConcurrentDictionary<string, string> _connectionToUser = new();
    
    // Lobi listesine erişim için referans (GlobalRooms'u burada da kullanacağız)
    // Static dictionary olduğu için doğrudan erişebiliriz.

    public async Task JoinRoom(string roomName, string userName, bool isProtected) 
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı global listeye ekle (Lobi için)
        // Buraya erişmek için Program sınıfındaki statik değişkene ihtiyaç var veya basitçe:
        // GlobalRooms statik olduğu için Hub içinden yönetilebilir.
        // Not: Bu örnekte oda oluşturma mantığı Join içindedir.
        ChatManager.AddRoom(roomName, isProtected);

        _connectionToRoom[Context.ConnectionId] = roomName;
        _connectionToUser[Context.ConnectionId] = userName;

        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"🚀 {userName} odaya giriş yaptı.");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
    {
        // Mesajı odaya dağıt
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.UtcNow);
    }

    public async Task SendSeen(string room, string user) => 
        await Clients.OthersInGroup(room).SendAsync("ReceiveSeen", user);

    public async Task SendTyping(string room, string user) => 
        await Clients.OthersInGroup(room).SendAsync("ReceiveTyping", user);

    // Kullanıcı bağlantısı koptuğunda (veya /exit yapıldığında)
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionToRoom.TryRemove(Context.ConnectionId, out var room) && 
            _connectionToUser.TryRemove(Context.ConnectionId, out var user))
        {
            await Clients.Group(room).SendAsync("ReceiveSystemMessage", $"🚪 {user} odadan ayrıldı.");
            
            // Eğer odada kimse kalmadıysa odayı listeden silebiliriz (Opsiyonel)
            // if (!_connectionToRoom.Values.Contains(room)) ChatManager.RemoveRoom(room);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

// --- YARDIMCI SINIFLAR VE MODELLER ---
public static class ChatManager {
    // Statik olarak odaları burada tutuyoruz
    public static ConcurrentDictionary<string, bool> GlobalRooms = new();
    public static void AddRoom(string name, bool isProtected) => GlobalRooms.TryAdd(name, isProtected);
}

public record UserDto(string Username, string Password);
public record ChatMessage(string User, string Msg, string Iv, bool IsFile, DateTime Time);
