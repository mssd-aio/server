using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Veritabanı ve SignalR servislerini ekle
builder.Services.AddDbContext<DbContext>(opt => opt.UseInMemoryDatabase("ChatDb"));
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();

app.UseCors();
// Mesajlaşma merkezini (Hub) tanımla
app.MapHub<ChatHub>("/chatHub");
app.MapGet("/", () => "Sunucu Calisiyor!");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// Mesaj Dağıtıcı Sınıf
public class ChatHub : Hub {
    public async Task JoinRoom(string roomName) => await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
        => await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.UtcNow);
}