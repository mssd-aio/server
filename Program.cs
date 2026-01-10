using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. VERİTABANI BAĞLANTISI VE SSL AYARI
var rawConnString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

if (string.IsNullOrEmpty(rawConnString))
{
    Console.WriteLine("[CRITICAL] CONNECTION_STRING NOT FOUND!");
}

// Render Postgres SSL gerektirir, bu yüzden dizeyi düzenliyoruz
var connString = rawConnString;
if (!string.IsNullOrEmpty(connString) && !connString.Contains("Trust Server Certificate"))
{
    connString += ";Trust Server Certificate=true;SSL Mode=Require;";
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connString));

builder.Services.AddSignalR().AddJsonProtocol();
builder.Services.AddCors();

var app = builder.Build();

// 2. VERİTABANINI VE TABLOLARI OTOMATİK OLUŞTUR
using (var scope = app.Services.CreateScope())
{
    try 
    {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Database.EnsureCreated();
        Console.WriteLine("[SYSTEM] Database connection successful and tables ensured.");
    }
    catch (Exception ex) 
    {
        Console.WriteLine($"[ERROR] Database check failed: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- API ENDPOINTLERİ ---

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
    return await db.Rooms.Select(r => new { Name = r.Name, IsProtected = r.IsProtected }).ToListAsync();
});

// Güncelleme için dosya indirme linki (Opsiyonel)
app.MapGet("/download", () => Results.File(Path.Combine(app.Environment.ContentRootPath, "SecureChat.exe"), "application/vnd.microsoft.portable-executable"));

app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- HUB MANTIĞI ---
public class ChatHub : Hub
{
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;

    public async Task JoinRoom(string roomName, string userName, bool isProtected)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);

        if (!await _db.Rooms.AnyAsync(r => r.Name == roomName))
        {
            _db.Rooms.Add(new RoomMeta { Name = roomName, IsProtected = isProtected });
            await _db.SaveChangesAsync();
        }

        var logs = await _db.Messages
            .Where(m => m.Room == roomName)
            .OrderBy(m => m.Time)
            .Take(50)
            .ToListAsync();

        foreach (var log in logs)
        {
            await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
        }
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile)
    {
        var timestamp = DateTime.Now;
        _db.Messages.Add(new MsgModel 
        { 
            Room = room, User = user, Msg = msg, Iv = iv, IsFile = isFile, Time = timestamp 
        });
        await _db.SaveChangesAsync();
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, timestamp);
    }
}

// --- VERİ MODELLERİ ---
public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
    public DbSet<RoomMeta> Rooms { get; set; }
    public DbSet<MsgModel> Messages { get; set; }
}

public class User { public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }
