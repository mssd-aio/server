using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// --- 1. VERİTABANI BAĞLANTISI (KESİN VE SAĞLAM ÇÖZÜM) ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string finalConnString = "";

Console.WriteLine(">>> BAGLANTI AYARLANIYOR...");

if (string.IsNullOrWhiteSpace(rawUrl))
{
    Console.WriteLine(">>> HATA: CONNECTION_STRING bulunamadi!");
}
else
{
    try
    {
        // Render URL'sini parse ediyoruz: postgres://user:pass@host:port/db
        var uri = new Uri(rawUrl);
        
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? userInfo[0] : "";
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        
        // HATA DUZELTME: Eger Uri portu okuyamazsa (-1 donerse) varsayilan 5432'yi kullaniyoruz.
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.Trim('/');
        var host = uri.Host;

        // Manuel string olusturuyoruz (Builder hatalarindan kaçinmak için)
        finalConnString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Require;Trust Server Certificate=true;Command Timeout=30;";
        
        Console.WriteLine($">>> URL PARSE EDILDI. Host: {host}, Port: {port}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> URL PARSE HATASI: {ex.Message}");
        // Parse edemezsek ham haliyle son sans deneriz (bazen ise yarar)
        finalConnString = rawUrl;
    }
}

// Sadece PostgreSQL kullaniyoruz, In-Memory sildik (Hata kaynagiydi)
builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- 2. VERİTABANI TEST VE TABLO OLUSTURMA ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        Console.WriteLine(">>> VERITABANINA BAGLANILIYOR...");
        // Veritabani yoksa olustur, varsa tablolari kontrol et
        db.Database.EnsureCreated();  
        Console.WriteLine(">>> VERITABANI BAGLANTISI VE TABLOLAR HAZIR <<<");
    }
    catch (Exception ex)
    {
        // Burasi cok kritik, hatayi terminale basiyoruz
        Console.WriteLine("**************************************************");
        Console.WriteLine($">>> KRITIK VERITABANI HATASI: {ex.Message}");
        if(ex.InnerException != null) Console.WriteLine($">>> DETAY: {ex.InnerException.Message}");
        Console.WriteLine("**************************************************");
    }
}

// --- 3. MIDDLEWARE ---
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
