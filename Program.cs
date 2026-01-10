using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTISI ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string finalConnString = "";

if (string.IsNullOrWhiteSpace(rawUrl))
{
    finalConnString = "Data Source=chat.db"; 
}
else
{
    try
    {
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        finalConnString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.Trim('/')};Username={userInfo[0]};Password={userInfo[1]};Ssl Mode=Require;Trust Server Certificate=true;";
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> BAGLANTI HATASI: {ex.Message}");
        finalConnString = rawUrl; 
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- 2. TABLOLARI ZORLA OLUŞTURMA (HATA GİDERİCİ) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        Console.WriteLine(">>> TABLOLAR KONTROL EDILIYOR...");
        // Bu satır tabloları (users, rooms, messages) otomatik oluşturur
        db.Database.EnsureCreated(); 
        Console.WriteLine(">>> VERITABANI VE TABLOLAR HAZIR.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> TABLO OLUSTURMA HATASI: {ex.Message}");
    }
}

// --- 3. MIDDLEWARE & API ---
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());

app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (string.IsNullOrWhiteSpace(u.Username) || string.IsNullOrWhiteSpace(u.Password)) return Results.BadRequest();
    if (await db.Users.AnyAsync(x => x.Username == u.Username)) return Results.Conflict();
    db.Users.Add(u);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/login", async (User u, ChatDbContext db) => {
    // Küçük harf tablo ismiyle sorgu yapar
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u.Username && x.Password == u.Password);
    return user != null ? Results.Ok() : Results.Unauthorized();
});

app.MapGet("/list-rooms", async (ChatDbContext db) => {
    return Results.Ok(await db.Rooms.Select(r => new { r.Name, r.IsProtected }).ToListAsync());
});

app.MapHub<ChatHub>("/chatHub");

var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{portEnv}");

// --- 4. MODELLER (POSTGRESQL ICIN KÜÇÜK HARF TABLO ISIMLERI) ---
[Table("users")] // Hatanın çözümü burası
public class User { 
    [Key] public int Id { get; set; } 
    public string Username { get; set; } = ""; 
    public string Password { get; set; } = ""; 
}

[Table("rooms")]
public class RoomMeta { 
    [Key] public int Id { get; set; } 
    public string Name { get; set; } = ""; 
    public bool IsProtected { get; set; } 
}

[Table("messages")]
public class MsgModel { 
    [Key] public int Id { get; set; } 
    public string Room { get; set; } = ""; 
    public string User { get; set; } = ""; 
    public string Msg { get; set; } = ""; 
    public string Iv { get; set; } = ""; 
    public bool IsFile { get; set; } 
    public DateTime Time { get; set; } 
}

public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RoomMeta> Rooms { get; set; } = null!;
    public DbSet<MsgModel> Messages { get; set; } = null!;
}

// --- 5. HUB ---
public class ChatHub : Hub {
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;

    public async Task JoinRoom(string r, string u, bool p) {
        await Groups.AddToGroupAsync(Context.ConnectionId, r);
        var exists = await _db.Rooms.AnyAsync(x => x.Name == r);
        if (!exists) {
            _db.Rooms.Add(new RoomMeta { Name = r, IsProtected = p });
            await _db.SaveChangesAsync();
        }
        var logs = await _db.Messages.Where(m => m.Room == r).OrderBy(m => m.Time).Take(50).ToListAsync();
        foreach (var log in logs) {
            await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
        }
    }

    public async Task SendMessage(string r, string u, string m, string i, bool f) {
        var t = DateTime.UtcNow;
        _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
        await _db.SaveChangesAsync();
        await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
    }
}
