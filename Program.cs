using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// --- LOBİ VERİSİ ---
var Users = new ConcurrentDictionary<string, string>();
var GlobalRooms = new ConcurrentDictionary<string, bool>(); // OdaAdı : IsProtected

app.MapPost("/register", (UserDto dto) => Users.TryAdd(dto.Username, dto.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (UserDto dto) => Users.TryGetValue(dto.Username, out var p) && p == dto.Password ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => GlobalRooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");
app.Run();

public class ChatHub : Hub {
    private static readonly ConcurrentDictionary<string, string> _rooms = new();
    private static readonly ConcurrentDictionary<string, string> _users = new();
    // DIŞARIDAKİ LİSTEYE ERİŞİM İÇİN YARDIMCI
    private static ConcurrentDictionary<string, bool> _globalRoomsRef = new(); 

    // ÖNEMLİ: Client 3 parametre gönderiyor, burası da 3 almalı!
    public async Task JoinRoom(string roomName, string userName, bool isProtected) {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Odayı lobi listesine ekle (Program içindeki listeye erişemediğimiz durumlar için hub içinde de tutulabilir)
        // Ancak en garantisi burada bir statik listeye eklemek:
        ChatManager.Rooms.TryAdd(roomName, isProtected);

        _rooms[Context.ConnectionId] = roomName;
        _users[Context.ConnectionId] = userName;
        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"🚀 {userName} odaya girdi.");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) {
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.UtcNow);
    }

    public override async Task OnDisconnectedAsync(Exception? ex) {
        if (_rooms.TryRemove(Context.ConnectionId, out var r) && _users.TryRemove(Context.ConnectionId, out var u))
            await Clients.Group(r).SendAsync("ReceiveSystemMessage", $"🚪 {u} ayrıldı.");
        await base.OnDisconnectedAsync(ex);
    }
}

public static class ChatManager {
    public static ConcurrentDictionary<string, bool> Rooms = new();
}
// Lobi API'sini ChatManager'a bağlayın:
// app.MapGet("/list-rooms", () => ChatManager.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

public record UserDto(string Username, string Password);
