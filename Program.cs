using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 10 * 1024 * 1024; });
builder.Services.AddCors();
var app = builder.Build();

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- DISCORD AYARI ---
// ÖNEMLİ: Eğer webhook kullanmayacaksan burayı "http://ignore.me" yap veya boş bırakma!
string discordWebhookUrl = "https://discord.com/api/webhooks/1459244500730773575/uLmWtFn3IKNckYMibFPw_zTZkvyZ3w5ThEmvbNJLnDGyQbrFZIBY5o6XP_RoJC4w-4L8";

app.MapPost("/register", (User u) => ChatHubStatic.Users.TryAdd(u.Username, u.Password) ? Results.Ok() : Results.BadRequest());
app.MapPost("/login", (User u) => {
    if (ChatHubStatic.Banned.Contains(u.Username)) return Results.Json(new { error = "BAN" }, statusCode: 403);
    return ChatHubStatic.Users.TryGetValue(u.Username, out var p) && p == u.Password ? Results.Ok() : Results.Unauthorized();
});
app.MapGet("/list-rooms", () => ChatHubStatic.Rooms.Select(r => new { Name = r.Key, IsProtected = r.Value }));

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub
{
    private async Task LogToDiscord(string msg) {
        // Hata veren kısım burasıydı. URL kontrolü eklendi.
        if (string.IsNullOrEmpty(ChatHubStatic.WebhookUrl) || !ChatHubStatic.WebhookUrl.StartsWith("http")) return;
        try {
            using var client = new HttpClient();
            await client.PostAsJsonAsync(ChatHubStatic.WebhookUrl, new { content = $"📡 **[LOG]** {msg}" });
        } catch { /* Discord hatası sistemi bozmasın */ }
    }

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        ChatHubStatic.Rooms[roomName] = isProtected;
        ChatHubStatic.UserCurrentRoom[userName] = roomName;
        
        await LogToDiscord($"**{userName}**, **{roomName}** odasına katıldı.");
        await Clients.Group(roomName).SendAsync("ReceiveMessage", "SYSTEM", $"{userName} katıldı.", "", false, DateTime.Now);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        await LogToDiscord($"✉️ **{user}** ({room}): {(isFile ? "[DOSYA]" : "Mesaj Gönderdi")}");
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.Now);
    }

    public async Task AdminCommand(string room, string action, string target, string extra = "")
    {
        bool isRoot = (extra == "kaytshine_token");
        if (action.ToUpper() == "BAN" && isRoot) ChatHubStatic.Banned.Add(target);
        if (action.ToUpper() == "ANNOUNCE" && isRoot) await Clients.All.SendAsync("ReceiveMessage", "GLOBAL DUYURU", target, "", false, DateTime.Now);
        if (action.ToUpper() == "KICK") await Clients.All.SendAsync("ReceiveAdminAction", "KICK", target);
    }
}

// Tüm verileri tutan statik sınıf (Sunucu kapanana kadar durur)
public static class ChatHubStatic {
    public static string WebhookUrl = "https://discord.com/api/webhooks/BURAYA_EKLE"; // BURAYI DOLDUR
    public static ConcurrentDictionary<string, string> Users = new();
    public static ConcurrentDictionary<string, bool> Rooms = new();
    public static ConcurrentDictionary<string, string> UserCurrentRoom = new();
    public static ConcurrentBag<string> Banned = new();
}
public record User(string Username, string Password);
