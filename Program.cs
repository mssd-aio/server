using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// --- VERİTABANI BAĞLANTISI ---
// Render üzerindeyseniz PostgreSQL, yereldeyseniz SQLite kullanabilirsiniz
var connString = Environment.GetEnvironmentVariable("CONNECTION_STRING") ?? "Data Source=chat.db";
builder.Services.AddDbContext<ChatDbContext>(opt => {
    if (connString.StartsWith("Host=")) opt.UseNpgsql(connString);
    else opt.UseSqlite(connString);
});

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// Veritabanını otomatik oluştur
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());

// API ENDPOINTLERİ
app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (await db.Users.AnyAsync(x => x.Username == u.Username)) return Results.Conflict();
    db.Users.Add(u);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/login", async (User u, ChatDbContext db) => {
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u.Username && x.Password == u.Password);
    return user != null ? Results.Ok() : Results.Unauthorized();
});

app.MapGet("/list-rooms", async (ChatDbContext db) => {
    return await db.Rooms.Select(r => new { r.Name, r.IsProtected }).ToListAsync();
});

app.MapHub<ChatHub>("/chatHub");

app.Run();

// --- MODELLER ---
public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
    public DbSet<RoomMeta> Rooms { get; set; }
    public DbSet<MessageLog> Messages { get; set; }
}

public class User { [Key] public int Id { get; set; } public string Username { get; set; } public string Password { get; set; } }
public class RoomMeta { [Key] public int Id { get; set; } public string Name { get; set; } public bool IsProtected { get; set; } }
public class MessageLog { [Key] public int Id { get; set; } public string Room { get; set; } public string User { get; set; } public string Content { get; set; } public string Iv { get; set; } public bool IsFile { get; set; } public DateTime Timestamp { get; set; } }

// --- SIGNALR HUB ---
public class ChatHub : Hub {
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;

    public async Task JoinRoom(string roomName, string username, bool isProtected) {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        
        // Oda yoksa oluştur
        if (!await _db.Rooms.AnyAsync(r => r.Name == roomName)) {
            _db.Rooms.Add(new RoomMeta { Name = roomName, IsProtected = isProtected });
            await _db.SaveChangesAsync();
        }

        // Geçmiş mesajları yükle (isteğe bağlı - son 20 mesaj)
        var history = await _db.Messages
            .Where(m => m.Room == roomName)
            .OrderByDescending(m => m.Timestamp)
            .Take(20)
            .Reverse()
            .ToListAsync();

        foreach (var m in history) {
            await Clients.Caller.SendAsync("ReceiveMessage", m.User, m.Content, m.Iv, m.IsFile, m.Timestamp);
        }
    }

    public async Task SendMessage(string roomName, string user, string msg, string iv, bool isFile) {
        var log = new MessageLog {
            Room = roomName,
            User = user,
            Content = msg,
            Iv = iv,
            IsFile = isFile,
            Timestamp = DateTime.UtcNow
        };
        _db.Messages.Add(log);
        await _db.SaveChangesAsync();

        await Clients.Group(roomName).SendAsync("ReceiveMessage", user, msg, iv, isFile, log.Timestamp);
    }
}
