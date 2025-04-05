# DebianRepository.NET - Simple Dynamic Debian Package Repository in .NET

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

## How to host your own Debian apt repository
### Running via Docker

```bash
docker build -t debianrepo .
docker run -p 5048:5048 -e GPG_PASSPHRASE="YOUR_GPG_PASSPHRASE" -e JWT_SECRET="YOUR_JWT_SECRET" -e DEB_STORAGE_PATH="/storage/" -v /path/to/private.asc:/private.asc -v /path/to/deb/file/storage:/storage debianrepo
```

### Debian Client Setup

```bash
curl -fsSL http://localhost:5048/debian/pubkey.gpg | gpg --dearmor | sudo tee /etc/apt/trusted.gpg.d/debrepo.gpg > /dev/null
echo "deb [signed-by=/etc/apt/trusted.gpg.d/debrepo.gpg] http://localhost:5048/repo stable main" | sudo tee /etc/apt/sources.list.d/debrepo.list
sudo apt update
sudo apt install your-package-name
```

### Notes

- You must have a RSA 4096 private-key in `.asc` format with a passphrase
- HTTPS is not configured in Docker (you can handle that via reverse proxy).
