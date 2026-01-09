using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(o => { o.EnableDetailedErrors = true; o.MaximumReceiveMessageSize = 10 * 1024 * 1024; });
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- DISCORD AYARI ---
string discordWebhookUrl = "https://discord.com/api/webhooks/1459244500730773575/uLmWtFn3IKNckYMibFPw_zTZkvyZ3w5ThEmvbNJLnDGyQbrFZIBY5o6XP_RoJC4w-4L8";

// VERİ DEPOLARI
var users = new ConcurrentDictionary<string, string>(); 
var bannedUsers = new ConcurrentBag<string>();

app.MapPost("/register", (User u) => users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => {
    if (bannedUsers.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    return users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized();
});
app.MapGet("/list-rooms", () => ChatHubStatic.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

// --- DISCORD LOG FONKSIYONU ---
async Task LogToDiscord(string content) {
    try {
        using var client = new HttpClient();
        var payload = new { content = $"📡 **[SERVER LOG]** {content}" };
        await client.PostAsJsonAsync(discordWebhookUrl, payload);
    } catch { /* Log hatası sunucuyu durdurmasın */ }
}

app.Run();

public class ChatHub : Hub
{
    // Static veri erişimi için (Hızlı çözüm)
    private static async Task QuickLog(string msg) {
        // Not: Normalde statik sınıftan HttpClient çağırmak yerine bir Service üzerinden yapılır.
        // Ama basitlik için webhook URL'sini buraya da ekleyebilirsin:
        string url = "BURAYA_DISCORD_WEBHOOK_URL_YAPISTIR";
        using var client = new HttpClient();
        await client.PostAsJsonAsync(url, new { content = msg });
    }

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatHubStatic.Rooms[roomName] = isProtected;
        ChatHubStatic.UserCurrentRoom[userName] = roomName;
        
        await QuickLog($"📥 **Giriş:** `{userName}` kullanıcısı `{roomName}` odasına katıldı.");
        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        // Mesaj loglama (Şifreli olduğu için mesaj içeriği Discord'da [Şifreli Veri] olarak görünür)
        string logMsg = isFile ? "📁 Bir dosya paylaştı." : "💬 Bir mesaj gönderdi.";
        await QuickLog($"✉️ **{user}** ({room}): {logMsg}");
        
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target, string extra = "")
    {
        bool isRoot = (extra == "kaytshine_token");
        await QuickLog($"🛠️ **Admin İşlemi:** `{action}` | Uygulayan: {(isRoot ? "ROOT" : "ADMIN")} | Hedef: `{target}`");

        switch (action.ToUpper())
        {
            case "BAN": if (isRoot) ChatHubStatic.Banned.Add(target); break;
            case "ANNOUNCE": await Clients.All.SendAsync("ReceiveMessage", "GLOBAL DUYURU", target, "", false, DateTime.Now); break;
            case "LIST_ALL": if (isRoot) await Clients.Caller.SendAsync("ReceiveSuperAdminPanel", ChatHubStatic.UserCurrentRoom.Select(x => $"{x.Key} ({x.Value})").ToList()); break;
            case "KICK": await Clients.All.SendAsync("ReceiveAdminAction", "KICK", target); break;
        }
    }
}

public static class ChatHubStatic {
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, string> UserCurrentRoom = new();
    public static ConcurrentBag<string> Banned = new();
}
public record User(string Username, string Password);
