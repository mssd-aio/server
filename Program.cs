using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR().AddJsonProtocol();
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// VERİ DEPOLARI
var users = new ConcurrentDictionary<string, string>(); 
var rooms = new ConcurrentDictionary<string, bool>();
// Her oda için mesaj geçmişini tutan liste: RoomName -> List<MessageData>
var roomLogs = new ConcurrentDictionary<string, List<MsgModel>>();

// API ENDPOINTLERİ
app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.Conflict());
app.MapPost("/login", (User u) => (users.TryGetValue(u.Username, out var p) && p == u.Password) ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub
{
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        rooms.TryAdd(roomName, isProtected);

        // Odaya yeni giren kişiye GEÇMİŞ MESAJLARI gönder
        if (roomLogs.TryGetValue(roomName, out var logs))
        {
            foreach (var log in logs.ToList()) // Listeyi kopyalayarak gönder (hata önleme)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
            }
        }
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        var timestamp = DateTime.Now;
        var newMsg = new MsgModel(user, msg, iv, isFile, timestamp);

        // Mesajı geçmişe kaydet
        var logs = roomLogs.GetOrAdd(room, _ => new List<MsgModel>());
        lock (logs) 
        {
            logs.Add(newMsg);
            if (logs.Count > 50) logs.RemoveAt(0); // Son 50 mesajı tutar
        }

        // Herkese yayınla
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, timestamp);
    }
}

// MODELLER
public record User(string Username, string Password);
public record MsgModel(string User, string Msg, string Iv, bool IsFile, DateTime Time);
