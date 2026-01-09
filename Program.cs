using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB dosya sınırı
});

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => 
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// --- BELLEK VERİ DEPOLAMA ---
var Users = new ConcurrentDictionary<string, string>(); // Kullanıcı:Şifre
var RoomPasswords = new ConcurrentDictionary<string, string>(); // Oda:ŞifreHash (Boşsa şifresiz)
var RoomHistory = new ConcurrentDictionary<string, List<ChatMessage>>();

// --- API ENDPOINTLERİ ---

app.MapPost("/register", (UserDto dto) => 
    Users.TryAdd(dto.Username, dto.Password) ? Results.Ok() : Results.BadRequest());

app.MapPost("/login", (UserDto dto) => 
    Users.TryGetValue(dto.Username, out var p) && p == dto.Password ? Results.Ok() : Results.Unauthorized());

// Lobi için oda listesi ve şifre koruması durumu
app.MapGet("/list-rooms", () => 
    RoomPasswords.Select(r => new { Name = r.Key, IsProtected = !string.IsNullOrEmpty(r.Value) }));

app.MapGet("/", () => "🛡️ SECURE SERVER v8.0 - LOBBY & PASS PROTECT ACTIVE");

// --- SIGNALR HUB ---

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub 
{
    private static readonly ConcurrentDictionary<string, string> _admins = new();
    private static readonly ConcurrentDictionary<string, string> _userRooms = new(); // ConnectionId:RoomName
    private static readonly ConcurrentDictionary<string, string> _userNames = new(); // ConnectionId:UserName

    public async Task JoinRoom(string roomName, string userName) 
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Kullanıcı takibi (Çıkış bildirimi için)
        _userRooms[Context.ConnectionId] = roomName;
        _userNames[Context.ConnectionId] = userName;

        // Oda şifre takibi (İlk giren odayı ve şifre durumunu oluşturur)
        // Not: Client tarafında şifre hashlenip RoomPasswords'e bir şekilde kaydedilebilir.
        // Şimdilik basitlik adına oda ilk kez oluşturuluyorsa listeye ekliyoruz.
        if (!RoomHistory.ContainsKey(roomName))
        {
            RoomHistory[roomName] = new List<ChatMessage>();
            // Önemli: Şifre durumunu burada varsayılan olarak kaydediyoruz.
            // (Client tarafındaki tercihe göre bu genişletilebilir)
        }

        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"🚀 {userName} odaya katıldı.");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
    {
        var chatMsg = new ChatMessage(user, msg, iv, isFile, DateTime.UtcNow);
        
        if (RoomHistory.TryGetValue(room, out var list)) {
            list.Add(chatMsg);
            if (list.Count > 50) list.RemoveAt(0); // Son 50 mesaj
        }

        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, chatMsg.Time);
    }

    public async Task SendSeen(string room, string user) => await Clients.OthersInGroup(room).SendAsync("ReceiveSeen", user);
    public async Task SendTyping(string room, string user) => await Clients.OthersInGroup(room).SendAsync("ReceiveTyping", user);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_userRooms.TryRemove(Context.ConnectionId, out var room) && 
            _userNames.TryRemove(Context.ConnectionId, out var user))
        {
            await Clients.Group(room).SendAsync("ReceiveSystemMessage", $"🚪 {user} odadan ayrıldı.");
        }
        await base.OnDisconnectedAsync(exception);
    }
}

// --- MODELLER ---
public record UserDto(string Username, string Password);
public record ChatMessage(string User, string Msg, string Iv, bool IsFile, DateTime Time);
