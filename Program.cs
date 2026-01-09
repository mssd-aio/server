using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB Dosya desteği
});
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.MapHub<ChatHub>("/chatHub");
app.MapGet("/", () => "Secure Server Active with History & File Support");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public class ChatHub : Hub {
    // Mesaj geçmişini oda bazlı tutmak için (Basit bir liste)
    private static readonly Dictionary<string, List<ChatMessage>> _history = new();

    public async Task JoinRoom(string roomName) {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Varsa geçmiş mesajları yeni gelene gönder
        if (_history.ContainsKey(roomName)) {
            foreach (var msg in _history[roomName]) {
                await Clients.Caller.SendAsync("ReceiveMessage", msg.User, msg.Msg, msg.Iv, msg.IsFile, msg.Time);
            }
        }
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) {
        var chatMsg = new ChatMessage(user, msg, iv, isFile, DateTime.UtcNow);
        
        // Geçmişe ekle
        if (!_history.ContainsKey(room)) _history[room] = new List<ChatMessage>();
        _history[room].Add(chatMsg);
        if (_history[room].Count > 50) _history[room].RemoveAt(0); // Son 50 mesajı tut

        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, chatMsg.Time);
    }
}

public record ChatMessage(string User, string Msg, string Iv, bool IsFile, DateTime Time);
