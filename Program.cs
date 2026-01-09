using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR().AddJsonProtocol();
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// API ENDPOINTLERİ
app.MapPost("/register", (User u) => ChatStore.Users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.Conflict());
app.MapPost("/login", (User u) => (ChatStore.Users.TryGetValue(u.Username, out var p) && p == u.Password) ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => ChatStore.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- HUB VE MANTIK ---
public class ChatHub : Hub
{
    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatStore.Rooms.TryAdd(roomName, isProtected);

        if (ChatStore.RoomLogs.TryGetValue(roomName, out var logs))
        {
            foreach (var log in logs.ToList())
            {
                await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
            }
        }
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        var timestamp = DateTime.Now;
        var newMsg = new MsgModel(user, msg, iv, isFile, timestamp);

        var logs = ChatStore.RoomLogs.GetOrAdd(room, _ => new List<MsgModel>());
        lock (logs)
        {
            logs.Add(newMsg);
            if (logs.Count > 50) logs.RemoveAt(0);
        }

        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, timestamp);
    }
}

// --- VERİ DEPOLAMA (Hata buradaydı, static içine alındı) ---
public static class ChatStore
{
    public static ConcurrentDictionary<string, string> Users = new();
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, List<MsgModel>> RoomLogs = new();
}

public record User(string Username, string Password);
public record MsgModel(string User, string Msg, string Iv, bool IsFile, DateTime Time);
