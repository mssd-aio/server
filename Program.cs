using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Gerekli Servisleri Ekle
builder.Services.AddSignalR();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials();
    });
});

var app = builder.Build();

// 2. Middleware Ayarları
app.UseCors();
app.UseRouting();

// 3. Endpoint Tanımları
app.MapHub<ChatHub>("/chatHub"); // İşte aranan endpoint burada!
app.MapGet("/", () => "Sunucu Calisiyor! ChatHub Aktif.");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// 4. EKSİK OLAN SINIF BURADA (Dosyanın en altına ekle)
public class ChatHub : Hub 
{
    public async Task JoinRoom(string roomName) 
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
    }

    public async Task SendMessage(string room, string user, string msg, string iv, bool isFile) 
    {
        await Clients.Group(room).SendAsync("ReceiveMessage", user, msg, iv, isFile, DateTime.UtcNow);
    }
}
