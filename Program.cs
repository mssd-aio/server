using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTISI (KESİN VE SAĞLAM ÇÖZÜM) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string finalConnString = "";

if (string.IsNullOrWhiteSpace(rawUrl))
{
    finalConnString = "Data Source=chat.db"; // Yerel test için
}
else
{
    try
    {
        // Render URL formatı: postgres://user:pass@host:port/db
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo[1];
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.Trim('/');

        // PostgreSQL için SSL ve Timeout ayarları eklendi
        finalConnString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Require;Trust Server Certificate=true;Command Timeout=30;";
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> BAGLANTI PARSE HATASI: {ex.Message}");
        finalConnString = rawUrl; // Hata olursa ham halini dene
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- 2. VERİTABANI TABLOLARINI ZORLA OLUŞTURMA ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        Console.WriteLine(">>> VERITABANI KONTROL EDILIYOR...");
        // EnsureCreated bazen PostgreSQL'de tabloyu görmeyebilir, bu yüzden manuel kontrol ekleyelim
        db.Database.EnsureCreated();
        Console.WriteLine(">>> TABLOLAR HAZIR.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> KRITIK DB HATASI: {ex.Message}");
    }
}

// --- 3. MIDDLEWARE & API ---
app.UseCors(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .SetIsOriginAllowed(_ => true)
    .AllowCredentials());

app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (string.IsNullOrWhiteSpace(u.Username) || string.IsNullOrWhiteSpace(u.Password)) return Results.BadRequest();
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
    var rooms = await db.Rooms.Select(r => new { r.Name, r.IsProtected }).ToListAsync();
    return Results.Ok(rooms);
});

app.MapHub<ChatHub>("/chatHub");

var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{portEnv}");

// --- 4. MODELLER (POSTGRESQL UYUMLU KÜÇÜK HARF ZORLAMASI) ---
[Table("users")]
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
        try {
            var exists = await _db.Rooms.AnyAsync(x => x.Name == r);
            if (!exists) {
                _db.Rooms.Add(new RoomMeta { Name = r, IsProtected = p });
                await _db.SaveChangesAsync();
            }
            // Geçmiş mesajlar
            var logs = await _db.Messages.Where(m => m.Room == r).OrderBy(m => m.Time).Take(50).ToListAsync();
            foreach (var log in logs) {
                await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
            }
        } catch (Exception ex) { Console.WriteLine($"Join Error: {ex.Message}"); }
    }

    public async Task SendMessage(string r, string u, string m, string i, bool f) {
        try {
            var t = DateTime.UtcNow;
            _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
            await _db.SaveChangesAsync();
            await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
        } catch (Exception ex) { Console.WriteLine($"Send Error: {ex.Message}"); }
    }
}
