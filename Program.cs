using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// --- BAĞLANTI DİZESİ DÜZELTİCİ (PORT HATASI İÇİN) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string connString = "";

if (!string.IsNullOrEmpty(rawUrl) && rawUrl.StartsWith("postgres"))
{
    try 
    {
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        var user = userInfo[0];
        var password = userInfo[1];
        var host = uri.Host;
        var database = uri.AbsolutePath.Trim('/');
        
        // Eğer port -1 gelirse default 5432 kullanıyoruz
        var port = uri.Port <= 0 ? 5432 : uri.Port;

        connString = $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true;";
        Console.WriteLine($"[SISTEM] Baglanti kuruluyor: {host}:{port}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[KRITIK] Link parcalama hatasi: {ex.Message}");
    }
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connString));

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// Veritabanını zorla oluştur (Tabloların gelmesi için)
using (var scope = app.Services.CreateScope())
{
    try {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Database.EnsureCreated();
        Console.WriteLine("[SISTEM] Veritabanı baglantisi ve tablolar OK.");
    } catch (Exception ex) {
        Console.WriteLine($"[HATA] DB Olusturulamadi: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// Kayıt ve Giriş API'leri
app.MapPost("/register", async (User u, ChatDbContext db) => {
    try {
        if (await db.Users.AnyAsync(x => x.Username == u.Username)) return Results.Conflict();
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return Results.Ok();
    } catch (Exception ex) {
        return Results.Problem("Kayit sirasinda DB hatasi: " + ex.Message);
    }
});

app.MapPost("/login", async (User u, ChatDbContext db) => {
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u.Username && x.Password == u.Password);
    return user != null ? Results.Ok() : Results.Unauthorized();
});

app.MapGet("/list-rooms", async (ChatDbContext db) => {
    return await db.Rooms.Select(r => new { Name = r.Name, IsProtected = r.IsProtected }).ToListAsync();
});

app.MapHub<ChatHub>("/chatHub");

var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{portEnv}");

// Modeller
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
