FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Paketleri önbelleğe almak için csproj kopyala ve restore et
COPY *.csproj ./
RUN dotnet restore

# Tüm dosyaları kopyala
COPY . .

# Yayınlama (Publish) öncesi temizlik ve derleme
# --no-restore parametresi hızı artırır, Release modu optimizasyon yapar
RUN dotnet publish -c Release -o out --no-restore

# Runtime aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# Render için gerekli port ayarları
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# DLL adının doğruluğundan emin ol (Proje adın neyse o olmalı)
ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
