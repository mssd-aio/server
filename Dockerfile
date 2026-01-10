FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Önbelleği temizle ve sadece gerekli olanı kopyala
COPY *.csproj ./
RUN dotnet restore

# Kalan her şeyi kopyala
COPY . .

# Hata veren obj klasörlerini sil ve temiz derleme yap
RUN rm -rf obj bin
RUN dotnet publish -c Release -o out

# Runtime aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
