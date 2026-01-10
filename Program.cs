using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- VERİTABANI BAĞLANTISI ---
var rawUrl = Environment.GetEnvironmentVariable("CONNECTION_STRING");
string finalConnString = "";

if (!string.IsNullOrEmpty(rawUrl))
{
    try {
        var dbUri = new Uri(rawUrl);
        var userInfo = dbUri.UserInfo.Split(':');
        var dbHost = dbUri.Host;
        var dbName = dbUri.AbsolutePath.Trim('/');
        var dbPort = dbUri.Port <= 0 ? 5432 : dbUri.Port; // Port hatasını çözer

        // Render Internal URL için en stabil format
        finalConnString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;";
    } catch {
        finalConnString = rawUrl; // Hata olursa ham halini dene
    }
}

builder.Services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(finalConnString));
builder.Services.AddSignalR();
builder.Services.AddCors();

var app = builder.Build();

// --- TABLOLARI OTOMATİK OLUŞTUR ---
using (var scope = app.Services.CreateScope())
{
    try {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        db.Database.EnsureCreated(); 
        Console.WriteLine(">>> VERITABANI BAGLANTISI BASARILI <<<");
    } catch (Exception ex) {
        Console.WriteLine($">>> BAGLANTI HATASI: {ex.Message}");
    }
}

app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

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

app.MapHub<ChatHub>("/chatHub");

// Değişken adını 'sunucuPort' yaparak çakışmayı (CS0136) engelledik
var sunucuPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{sunucuPort}");

public class ChatDbContext : DbContext {
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
}
public class User { public int Id { get; set; } public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class ChatHub : Hub { }
