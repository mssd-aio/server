FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Dosyaları kopyala ve restore et
COPY . .
RUN dotnet restore "SecureChatServer.csproj"

# Yayınla
RUN dotnet publish "SecureChatServer.csproj" -c Release -o out

# Çalışma zamanı imajı
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
