FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Sadece projeyi çek
COPY *.csproj ./
RUN dotnet restore

# Kalanı kopyala
COPY . .

# Bin ve obj klasörlerini temizlediğimizden emin olalım
RUN rm -rf bin obj

# Yayınla
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
