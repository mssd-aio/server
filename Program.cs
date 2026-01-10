using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render'daki Environment Variable'ı okur
var connString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connString));

builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// Veritabanını otomatik hazırlar
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

app.MapPost("/register", async (User u, ChatDbContext db) => {
    if (await db.Users.AnyAsync(x => x.Username == u.Username)) return Results.Conflict();
    db.Users.Add(u); await db.SaveChangesAsync(); return Results.Ok();
});

app.MapPost("/login", async (User u, ChatDbContext db) => {
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u.Username && x.Password == u.Password);
    return user != null ? Results.Ok() : Results.Unauthorized();
});

app.MapGet("/list-rooms", async (ChatDbContext db) => 
    await db.Rooms.Select(r => new { r.Name, r.IsProtected }).ToListAsync());

app.MapHub<ChatHub>("/chatHub");

app.Run();

public class ChatHub : Hub {
    private readonly ChatDbContext _db;
    public ChatHub(ChatDbContext db) => _db = db;

    public async Task JoinRoom(string roomName, string userName, bool isProtected) {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        if (!await _db.Rooms.AnyAsync(r => r.Name == roomName)) {
            _db.Rooms.Add(new RoomMeta { Name = roomName, IsProtected = isProtected });
            await _db.SaveChangesAsync();
        }
        var logs = await _db.Messages.Where(m => m.Room == roomName).OrderBy(m => m.Time).Take(50).ToListAsync();
        foreach (var l in logs) await Clients.Caller.SendAsync("ReceiveMessage", l.User, l.Msg, l.Iv, l.IsFile, l.Time);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) {
        var t = DateTime.Now;
        _db.Messages.Add(new MsgModel { Room = room, User = user, Msg = msg, Iv = iv, IsFile = isFile, Time = t });
        await _db.SaveChangesAsync();
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, t);
    }
}

public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
    public DbSet<RoomMeta> Rooms { get; set; }
    public DbSet<MsgModel> Messages { get; set; }
}

public class User { public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RoomMeta { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsProtected { get; set; } }
public class MsgModel { public int Id { get; set; } public string Room { get; set; } = ""; public string User { get; set; } = ""; public string Msg { get; set; } = ""; public string Iv { get; set; } = ""; public bool IsFile { get; set; } public DateTime Time { get; set; } }
