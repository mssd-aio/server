using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Npgsql; // Npgsql paketinin yüklü oldugundan emin ol

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTISI (KESİN PORT DÜZELTMESİ) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING")?.Trim();
string finalConnString = "";

Console.WriteLine(">>> BAGLANTI AYARLANIYOR...");

if (string.IsNullOrEmpty(rawUrl))
{
    Console.WriteLine(">>> HATA: CONNECTION_STRING bos! Render Environment Variables kismini kontrol et.");
}
else
{
    try
    {
        // Render'in verdigi postgres:// formatini parse ediyoruz
        var uri = new Uri(rawUrl);
        
        // Sifre icinde ':' olabilir, bu yuzden sadece ilk ':' isaretinden boluyoruz
        var userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
        var username = userInfo.Length > 0 ? userInfo[0] : "";
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        
        // HATA DUZELTME BURADA: Port -1 gelirse 5432 yapiyoruz
        var port = uri.Port > 0 ? uri.Port : 5432;

        var connBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = port,
            Username = username,
            Password = password,
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        finalConnString = connBuilder.ToString();
        Console.WriteLine($">>> URL PARSE EDILDI. Host: {uri.Host}, Port: {port}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> URL PARSE HATASI: {ex.Message}");
        // Hata durumunda bile ham URL'yi deneme, format yanlis oldugu icin patlar.
        // O yuzden finalConnString bos kalir ve asagida log duser.
    }
}

// DbContext Ekleme
if (string.IsNullOrEmpty(finalConnString))
{
    // Hata durumunda uygulama cokmemesi icin hafiza ici db aciyoruz (Gecici cozum)
    Console.WriteLine(">>> UYARI: Gecerli baglanti dizesi olusturulamadi. In-Memory DB kullanilacak (Veriler kaybolur).");
    builder.Services.AddDbContext<ChatDbContext>(options => options.UseInMemoryDatabase("TempDb"));
}
else
{
    builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
}

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- 2. VERİTABANI BAĞLANTI TESTİ ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        if (!string.IsNullOrEmpty(finalConnString))
        {
            Console.WriteLine(">>> VERITABANI BAGLANTISI DENENIYOR...");
            db.Database.OpenConnection(); // Baglantiyi test et
            db.Database.EnsureCreated();  // Tablolari olustur
            Console.WriteLine(">>> VERITABANI BAGLANTISI BASARILI <<<");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("**************************************************");
        Console.WriteLine($">>> KRITIK VERITABANI HATASI: {ex.Message}");
        Console.WriteLine("**************************************************");
        // Hata olsa bile devam et ki loglari okuyabilelim
    }
}

// --- 3. MIDDLEWARE AYARLARI ---
app.UseCors(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .SetIsOriginAllowed(_ => true)
    .AllowCredentials());

// API ENDPOINTLERİ
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
    return await db.Rooms.Select(r => new { r.Name, r.IsProtected }).ToListAsync();
});

app.MapHub<ChatHub>("/chatHub");

var portEnv = Environment.GetEnvironmentVariable("PORT") ?? "8080";
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

// --- HUB ---
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
            var logs = await _db.Messages.Where(m => m.Room == r).OrderBy(m => m.Time).Take(50).ToListAsync();
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
            await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
        } catch (Exception ex) {
             Console.WriteLine($"Send Error: {ex.Message}");
        }
    }
}
