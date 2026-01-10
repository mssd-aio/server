using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// 1. VERİTABANI BAĞLANTISI (REGEX İLE GÜVENLİ AYRIŞTIRMA)
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING")?.Trim();
string finalConnString = "";

if (!string.IsNullOrEmpty(rawUrl))
{
    // Regex ile postgres://user:pass@host:port/db kalıbını parçalıyoruz
    var match = Regex.Match(rawUrl, @"postgres://(?<user>[^:]+):(?<pass>[^@]+)@(?<host>[^:/]+):(?<port>\d+)/(?<db>.+)");
    
    if (match.Success)
    {
        finalConnString = $"Host={match.Groups["host"].Value};" +
                          $"Port={match.Groups["port"].Value};" +
                          $"Database={match.Groups["db"].Value};" +
                          $"Username={match.Groups["user"].Value};" +
                          $"Password={match.Groups["pass"].Value};" +
                          $"SSL Mode=Require;Trust Server Certificate=true;";
    }
    else
    {
        // Eğer regex eşleşmezse ham haliyle kullanmayı dene
        finalConnString = rawUrl.Replace("postgres://", "postgresql://");
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
builder.Services.AddCors();

var app = builder.Build();

// 2. VERİTABANI BAĞLANTISINI TEST ET VE OLUŞTUR
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try 
    {
        db.Database.EnsureCreated();
        Console.WriteLine(">>> VERITABANI BAGLANTISI VE TABLOLAR TAMAM <<<");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> VERITABANI HATASI: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());

// API ENDPOINTLERİ
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
