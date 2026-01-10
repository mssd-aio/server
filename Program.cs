using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// 1. VERİTABANI BAĞLANTISI (KESİN ÇÖZÜM FORMATI)
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING")?.Trim();
string finalConnString = "";

if (!string.IsNullOrEmpty(rawUrl) && rawUrl.Contains("@"))
{
    try 
    {
        // Örn: postgres://user:pass@host:port/db
        var uri = new Uri(rawUrl);
        var userInfo = uri.UserInfo.Split(':');
        var user = userInfo[0];
        var pass = userInfo[1];
        var host = uri.Host;
        var port = uri.Port;
        var database = uri.AbsolutePath.Trim('/');

        finalConnString = $"Host={host};Port={port};Database={database};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=true";
    }
    catch 
    {
        finalConnString = rawUrl; // Hata olursa ham halini dene
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// 2. VERİTABANI BAĞLANTI TESTİ
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try 
    {
        // Veritabanı bağlantısını zorla açmayı dene
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        Console.WriteLine(">>> VERITABANI BAGLANTISI BASARILI <<<");
    }
    catch (Exception ex)
    {
        // Hata varsa terminalde açıkça göreceğiz
        Console.WriteLine($">>> BAGLANTI HATASI: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());

// API ENDPOINTLERİ
app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (await db.Users.AnyAsync(x => x.Username == u.Username)) return Results.Conflict();
    db.Users.Add(u); await db.SaveChangesAsync(); return Results.Ok();
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

// MODELLER
public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RoomMeta> Rooms { get; set; } = null!;
    public DbSet<MsgModel> Messages { get; set; } = null!;
}
public class User { [Key] public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { [Key] public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { [Key] public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }

// HUB
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
        var t = DateTime.UtcNow;
        _db.Messages.Add(new MsgModel { Room = r, User = u, Msg = m, Iv = i, IsFile = f, Time = t });
        await _db.SaveChangesAsync();
        await Clients.Group(r).SendAsync("ReceiveMessage", u, m, i, f, t);
    }
}
