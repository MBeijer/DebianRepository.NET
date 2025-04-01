# DebianRepo - Dynamic Debian Package Repository in .NET

This project is a self-contained Debian APT repository written in C# using ASP.NET Core MVC. It allows uploading `.deb` files, dynamically generating the `Packages` and `Release` files, and serving them with GPG signing—all in memory.

## Features

- Upload `.deb` files via API
- Parses and indexes `.deb` metadata
- Dynamically generates `Packages` and `Release` files
- GPG signs the `Release` file using system GPG
- Fully functional APT repository (no static files)

## Endpoints

- `POST /repo/upload` - Upload a `.deb` file
- `GET /repo/dists/stable/Release` - Signed release metadata
- `GET /repo/dists/stable/main/binary-amd64/Packages` - Package index
- `GET /repo/pool/main/{filename}` - Download `.deb` file

## Running via Docker

```bash
docker build -t debianrepo .
docker run -p 5000:80 -e GPG_KEY_ID="YOUR_KEY_ID" debianrepo
```

## Debian Client Setup

```bash
echo "deb [trusted=yes] http://localhost:5000/repo stable main" | sudo tee /etc/apt/sources.list.d/custom.list
sudo apt update
sudo apt install your-package-name
```

## Notes

- You must have a GPG key installed on the host and pass `GPG_KEY_ID` via environment variable.
- HTTPS is not configured in Docker (you can handle that via reverse proxy).

---

## Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "DebianRepo.csproj"
RUN dotnet build "DebianRepo.csproj" -c Release -o /app/build
RUN dotnet publish "DebianRepo.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "DebianRepo.dll"]
```
