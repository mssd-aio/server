using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTISI (RENDER & POSTGRES UYUMLU) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string finalConnString = "";

if (string.IsNullOrWhiteSpace(rawUrl))
{
    // Yerel test için SQLite, Render'da mutlaka hata basar
    finalConnString = "Data Source=chat.db";
    Console.WriteLine(">>> UYARI: CONNECTION_STRING bulunamadi, SQLite kullaniliyor.");
}
else
{
    try
    {
        // Render URL'sini (postgres://user:pass@host:port/db) Npgsql formatına çeviriyoruz
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo[1];
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.Trim('/');

        finalConnString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Require;Trust Server Certificate=true;Include Error Detail=true;";
        Console.WriteLine($">>> VERITABANI BAGLANTISI YAPILANDIRILDI: {host}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> URL PARSE HATASI: {ex.Message}");
        finalConnString = rawUrl; 
    }
}

// DbContext Servisini Ekle
builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));

builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true; // Hata ayıklama için kritik
});

builder.Services.AddCors();

var app = builder.Build();

// --- 2. TABLOLARI ZORLA OLUŞTUR (RELATION DOES NOT EXIST HATASI ÇÖZÜMÜ) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        Console.WriteLine(">>> TABLOLAR KONTROL EDILIYOR...");
        db.Database.EnsureCreated(); // Tablolar yoksa oluşturur
        Console.WriteLine(">>> VERITABANI VE TABLOLAR HAZIR.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"**************************************");
        Console.WriteLine($">>> KRITIK DB HATASI: {ex.Message}");
        Console.WriteLine($"**************************************");
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

// Render PORT ayarı
var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{portEnv}");

// --- 4. MODELLER ---
public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RoomMeta> Rooms { get; set; } = null!;
    public DbSet<MsgModel> Messages { get; set; } = null!;
}

public class User { [Key] public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { [Key] public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { [Key] public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }

// --- 5. HUB (CLIENT ILE TAM UYUMLU) ---
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
            // Son 50 mesajı gönder
            var logs = await _db.Messages
                .Where(m => m.Room == r)
                .OrderBy(m => m.Time)
                .Take(50)
                .ToListAsync();

            foreach (var log in logs) {
                await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
            }
        } catch (Exception ex) {
             Console.WriteLine($"Join Error: {ex.Message}");
        }
    }

    public async Task SendMessage(string r, string u, string m, string i, bool f) {
        try {
            var t = DateTime.UtcNow;
            _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
            await _db.SaveChangesAsync();
            // Gelen mesajı gruptaki herkese yay
            await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
        } catch (Exception ex) {
             Console.WriteLine($"Send Error: {ex.Message}");
        }
    }
}
