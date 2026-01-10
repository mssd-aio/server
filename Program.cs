using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTI AYARI (Hatasız Port Çözümü) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string connString = "";

if (!string.IsNullOrEmpty(rawUrl))
{
    try 
    {
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        var user = userInfo[0];
        var password = userInfo[1];
        var host = uri.Host;
        var dbName = uri.AbsolutePath.Trim('/');
        
        // KRİTİK: Port -1 gelirse Npgsql hata verir. Elle 5432 atıyoruz.
        var port = uri.Port <= 0 ? 5432 : uri.Port;

        connString = $"Host={host};Port={port};Database={dbName};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true;";
        Console.WriteLine($"[SİSTEM] Veritabanı hedefi belirlendi: {host}:{port}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HATA] Bağlantı linki işlenemedi: {ex.Message}");
    }
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connString));

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- 2. VERİTABANI VE TABLO OLUŞTURMA ---
using (var scope = app.Services.CreateScope())
{
    try {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Database.EnsureCreated(); // Tablolar yoksa otomatik oluşturur
        Console.WriteLine("[BAŞARILI] Veritabanı bağlantısı ve tablolar hazır.");
    } catch (Exception ex) {
        Console.WriteLine($"[HATA] Veritabanına ulaşılamıyor: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// --- 3. API ENDPOINTLERİ ---

app.MapPost("/register", async (User u, ChatDbContext db) => {
    try {
        if (string.IsNullOrWhiteSpace(u.Username) || string.IsNullOrWhiteSpace(u.Password))
            return Results.BadRequest("Kullanıcı adı veya şifre boş olamaz.");

        if (await db.Users.AnyAsync(x => x.Username == u.Username)) 
            return Results.Conflict();

        db.Users.Add(u);
        await db.SaveChangesAsync();
        Console.WriteLine($"[KAYIT] Yeni kullanıcı: {u.Username}");
        return Results.Ok();
    } catch (Exception ex) {
        Console.WriteLine($"[HATA] Kayıt sırasında DB hatası: {ex.Message}");
        return Results.Problem("Sunucu veritabanı hatası.");
    }
});

app.MapPost("/login", async (User u, ChatDbContext db) => {
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u.Username && x.Password == u.Password);
    if (user != null) {
        Console.WriteLine($"[GİRİŞ] Kullanıcı bağlandı: {u.Username}");
        return Results.Ok();
    }
    return Results.Unauthorized();
});

app.MapGet("/list-rooms", async (ChatDbContext db) => {
    return await db.Rooms.Select(r => new { Name = r.Name, IsProtected = r.IsProtected }).ToListAsync();
});

app.MapHub<ChatHub>("/chatHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// --- 4. MODELLER VE HUB ---
public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
    public DbSet<RoomMeta> Rooms { get; set; }
    public DbSet<MsgModel> Messages { get; set; }
}

public class User { public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }

public class ChatHub : Hub {
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;
    public async Task JoinRoom(string r, string u, bool p) {
        await Groups.AddToGroupAsync(Context.ConnectionId, r);
        if (!await _db.Rooms.AnyAsync(x => x.Name == r)) {
            _db.Rooms.Add(new RoomMeta { Name = r, IsProtected = p });
            await _db.SaveChangesAsync();
        }
        var logs = await _db.Messages.Where(m => m.Room == r).OrderBy(m => m.Time).Take(50).ToListAsync();
        foreach (var log in logs) await Clients.Caller.SendAsync("ReceiveMessage", log.User, log.Msg, log.Iv, log.IsFile, log.Time);
    }
    public async Task SendMessage(string r, string u, string m, string i, bool f) {
        var t = DateTime.Now;
        _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
        await _db.SaveChangesAsync();
        await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
    }
}
