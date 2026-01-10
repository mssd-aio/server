FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Sadece proje dosyasını kopyalayıp paketleri çekiyoruz
COPY *.csproj ./
RUN dotnet restore

# Kalan tüm dosyaları kopyalıyoruz
COPY . .

# Bin ve obj klasörlerini temizleyip öyle yayınlıyoruz (Kritik adım)
RUN rm -rf bin obj
RUN dotnet publish -c Release -o out

# Runtime aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
