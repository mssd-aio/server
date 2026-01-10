using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// 1. VERİTABANI BAĞLANTISI (Render Uyumluluğu Artırıldı)
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING")?.Trim();
string finalConnString = "";

if (!string.IsNullOrEmpty(rawUrl)) {
    if (rawUrl.StartsWith("postgres://") || rawUrl.StartsWith("postgresql://")) {
        try {
            var uri = new Uri(rawUrl);
            var userInfo = uri.UserInfo.Split(':');
            // Render'ın beklediği tüm parametreleri güvenli bir şekilde oluşturuyoruz
            finalConnString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.Trim('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;";
        } catch {
            finalConnString = rawUrl; // Ayrıştırma hatası olursa ham haliyle dene
        }
    } else {
        finalConnString = rawUrl;
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => 
    options.UseNpgsql(finalConnString));

builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true; // Hataları istemci tarafında görebilmek için
});

builder.Services.AddCors();

var app = builder.Build();

// 2. VERİTABANI OLUŞTURMA VE TABLOLARI HAZIRLAMA
using (var scope = app.Services.CreateScope()) {
    try {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Database.EnsureCreated(); // Tablolar yoksa oluşturur
        Console.WriteLine(">>> VERITABANI BAGLANTISI BASARILI <<<");
    } catch (Exception ex) {
        Console.WriteLine($">>> VERITABANI HATASI: {ex.Message}");
    }
}

// 3. MIDDLEWARE VE CORS
app.UseCors(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .SetIsOriginAllowed(_ => true)
    .AllowCredentials());

// API ENDPOINTLERİ
app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (string.IsNullOrEmpty(u.Username) || string.IsNullOrEmpty(u.Password)) return Results.BadRequest();
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

// Render için Port Ayarı
var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "10000";
app.Run($"http://0.0.0.0:{portEnv}");

// --- MODELLER ---
public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RoomMeta> Rooms { get; set; } = null!;
    public DbSet<MsgModel> Messages { get; set; } = null!;
}

public class User { [Key] public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { [Key] public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { [Key] public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }

// --- HUB (İSTEMCİYLE TAM UYUMLU) ---
public class ChatHub : Hub {
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;

    public async Task JoinRoom(string r, string u, bool p) {
        try {
            await Groups.AddToGroupAsync(Context.ConnectionId, r);
            
            // Odayı kaydet
            var room = await _db.Rooms.FirstOrDefaultAsync(x => x.Name == r);
            if (room == null) {
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
            Console.WriteLine($"JoinRoom Hatasi: {ex.Message}");
        }
    }

    public async Task SendMessage(string r, string u, string m, string i, bool f) {
        try {
            var t = DateTime.UtcNow;
            _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
            await _db.SaveChangesAsync();
            await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
        } catch (Exception ex) {
            Console.WriteLine($"SendMessage Hatasi: {ex.Message}");
        }
    }
}
