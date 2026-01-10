FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Önce sadece projeyi kopyala ve restore et (Önbellek dostu)
COPY *.csproj ./
RUN dotnet restore

# Kalan her şeyi kopyala
COPY . .

# ÖNEMLİ: Derlemeden önce her şeyi temizle
RUN dotnet clean
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SecureChatServer.dll"]
