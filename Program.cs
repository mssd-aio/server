using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => 
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();

// API Endpoints
app.MapPost("/register", (UserDto dto) => ChatData.Users.TryAdd(dto.Username, dto.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (UserDto dto) => ChatData.Users.TryGetValue(dto.Username, out var p) && p == dto.Password ? Results.Ok() : Results.Unauthorized());
app.MapGet("/list-rooms", () => ChatData.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));
app.MapHub<ChatHub>("/chatHub");
app.Run();

// Data Storage
public static class ChatData {
    public static ConcurrentDictionary<string, string> Users = new();
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, List<ChatMessage>> History = new();
}

public class ChatHub : Hub {
    public async Task JoinRoom(string roomName, string userName, bool isProtected) {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatData.Rooms.TryAdd(roomName, isProtected);
        if (ChatData.History.TryGetValue(roomName, out var history)) {
            foreach (var m in history.ToArray()) 
                await Clients.Caller.SendAsync("ReceiveMessage", m.User, m.Msg, m.Iv, m.IsFile, m.Time);
        }
        await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"SISTEM: {userName} baglandi.");
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) {
        var chatMsg = new ChatMessage(user, msg, iv, isFile, DateTime.UtcNow);
        var history = ChatData.History.GetOrAdd(room, _ => new List<ChatMessage>());
        lock(history) { history.Add(chatMsg); if (history.Count > 50) history.RemoveAt(0); }
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, chatMsg.Time);
    }
}

public record UserDto(string Username, string Password);
public record ChatMessage(string User, string Msg, string Iv, bool IsFile, DateTime Time);
