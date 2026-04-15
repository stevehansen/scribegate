# Self-Hosting Guide

Scribegate is designed to be trivially easy to self-host. It's a single binary with a single SQLite file for data. No external databases, no message queues, no caches.

## Requirements

- **Runtime:** .NET 10 (or Docker, which bundles it)
- **OS:** Windows, Linux, or macOS
- **RAM:** 64 MB minimum, 256 MB recommended
- **Disk:** 100 MB for the app + your document data
- **Network:** One HTTP port (default 8080)

## Option 1: Docker (Recommended)

The simplest path. One command, zero configuration.

```bash
docker run -d \
  --name scribegate \
  -p 8080:8080 \
  -v scribegate-data:/data \
  --restart unless-stopped \
  ghcr.io/scribegate/scribegate:latest
```

That's it. Open `http://localhost:8080`.

### Docker Compose

For more control, use a `docker-compose.yml`:

```yaml
services:
  scribegate:
    image: ghcr.io/scribegate/scribegate:latest
    ports:
      - "8080:8080"
    volumes:
      - scribegate-data:/data
    environment:
      - Scribegate__BaseUrl=https://docs.example.com
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz"]
      interval: 30s
      timeout: 5s
      retries: 3

volumes:
  scribegate-data:
```

```bash
docker compose up -d
```

### Updating

```bash
docker compose pull
docker compose up -d
```

Migrations run automatically on startup. Your data is preserved in the volume.

## Option 2: dotnet publish

For running on bare metal or in environments where Docker isn't available.

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet publish src/Scribegate.Web -c Release -o ./publish
```

Run it:

```bash
cd publish
./Scribegate.Web
```

Or on Windows:

```powershell
cd publish
.\Scribegate.Web.exe
```

### Running as a Service

**Linux (systemd):**

```ini
# /etc/systemd/system/scribegate.service
[Unit]
Description=Scribegate
After=network.target

[Service]
Type=exec
WorkingDirectory=/opt/scribegate
ExecStart=/opt/scribegate/Scribegate.Web
Environment=ASPNETCORE_URLS=http://+:8080
Environment=Scribegate__DataPath=/var/lib/scribegate
Restart=always
RestartSec=5
User=scribegate

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now scribegate
```

**Windows (as a service):**

```powershell
sc.exe create Scribegate binPath="C:\scribegate\Scribegate.Web.exe" start=auto
sc.exe start Scribegate
```

## Option 3: Azure App Service

### Free Tier (F1)

Good for evaluation. Limits: 60 CPU-minutes/day, 1 GB RAM, no custom domain.

1. Create a new Web App in the Azure Portal
2. Set the runtime stack to **.NET 10**
3. Deploy via GitHub Actions, VS Code, or `az webapp deploy`
4. Set the application setting: `Scribegate__DataPath` = `/home/data`

The `/home` directory is persistent on Azure App Service.

### Basic Tier (B1) — ~$13/month

For production use with custom domains and always-on.

Same setup as F1, but with:
- Custom domain support
- Always-on (no cold starts)
- More CPU and memory

## Option 4: fly.io

```bash
# Install flyctl if you haven't
curl -L https://fly.io/install.sh | sh

# Launch
fly launch --image ghcr.io/scribegate/scribegate:latest

# Create a persistent volume
fly volumes create scribegate_data --size 1

# Set the data path
fly secrets set Scribegate__DataPath=/data
```

The free tier includes 3 shared-cpu VMs — plenty for Scribegate.

## Configuration Reference

All configuration can be set via environment variables or `appsettings.json`.

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `Scribegate__DataPath` | `data` | Directory for the SQLite database file. Created automatically if it doesn't exist. |
| `Scribegate__BaseUrl` | `http://localhost:8080` | Public URL of the instance. Used for links in notifications and emails. |
| `ASPNETCORE_URLS` | `http://+:8080` | HTTP listen address. Set to `http://+:443` if terminating TLS directly. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Set to `Development` for detailed error pages. **Never use Development in production.** |

### appsettings.json

```json
{
  "Scribegate": {
    "DataPath": "/var/lib/scribegate",
    "BaseUrl": "https://docs.example.com"
  }
}
```

## HTTPS / TLS

Scribegate itself serves HTTP. For HTTPS, use a reverse proxy:

### Caddy (simplest)

```
docs.example.com {
    reverse_proxy localhost:8080
}
```

Caddy auto-provisions Let's Encrypt certificates. No configuration beyond this.

### nginx

```nginx
server {
    listen 443 ssl;
    server_name docs.example.com;

    ssl_certificate /etc/letsencrypt/live/docs.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/docs.example.com/privkey.pem;

    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Backup and Restore

### Backup

The entire Scribegate state is one SQLite file:

```bash
# Simple file copy (stop the app first, or accept a brief moment of inconsistency)
cp /data/scribegate.db /backups/scribegate-$(date +%Y%m%d).db

# Zero-downtime backup using SQLite's backup API
sqlite3 /data/scribegate.db ".backup /backups/scribegate-$(date +%Y%m%d).db"
```

### Restore

```bash
# Stop the app
docker compose down

# Replace the database
cp /backups/scribegate-20260415.db /data/scribegate.db

# Start the app (migrations will run if needed)
docker compose up -d
```

### Automated Backup Script

```bash
#!/bin/bash
# backup-scribegate.sh — run via cron
BACKUP_DIR="/backups/scribegate"
DATA_DIR="/data"
RETENTION_DAYS=30

mkdir -p "$BACKUP_DIR"
sqlite3 "$DATA_DIR/scribegate.db" ".backup $BACKUP_DIR/scribegate-$(date +%Y%m%d-%H%M).db"

# Clean old backups
find "$BACKUP_DIR" -name "scribegate-*.db" -mtime +$RETENTION_DAYS -delete
```

Add to cron:
```bash
# Daily at 2 AM
0 2 * * * /opt/scribegate/backup-scribegate.sh
```

## Troubleshooting

### The app won't start

**Symptom:** "Unable to open database file" or "SQLite error: disk I/O error"

**Fix:** Check that the data directory exists and is writable by the app's user:
```bash
ls -la /data/
# Should show the directory with write permissions for the app user
```

If using Docker, ensure the volume is mounted:
```bash
docker inspect scribegate | grep -A 5 Mounts
```

### Health check fails

**Symptom:** `GET /healthz` returns `503 Unhealthy`

**Fix:** The database connection is broken. Check:
1. Does the SQLite file exist? `ls /data/scribegate.db`
2. Is the disk full? `df -h /data/`
3. Are there permission issues? The app needs read/write access to both the `.db` file and its directory (SQLite creates `-wal` and `-shm` files alongside it)

### Migration fails on startup

**Symptom:** App crashes with a migration error in the logs

**Fix:** This usually means the database file is corrupted or from an incompatible version:
1. Check the logs for the specific migration error
2. If the database is empty/new, delete it and let the app recreate it: `rm /data/scribegate.db*`
3. If you have data, restore from a backup and try again
4. If upgrading from a much older version, check the release notes for migration steps

### Port already in use

**Symptom:** "Failed to bind to address" or "Address already in use"

**Fix:**
```bash
# Find what's using the port
lsof -i :8080
# or on Windows
netstat -ano | findstr :8080

# Change the port
# Docker: -p 9090:8080
# Direct: ASPNETCORE_URLS=http://+:9090
```

### SQLite database is locked

**Symptom:** "database is locked" errors under load

**Fix:** SQLite handles concurrent reads well but serializes writes. For most Scribegate workloads (many reads, few writes), this is fine. If you're hitting lock contention:
1. Ensure WAL mode is enabled (it is by default — check with `PRAGMA journal_mode;`)
2. Keep write transactions short (Scribegate does this internally)
3. If you're running multiple Scribegate instances against the same file: don't. Use one instance with a reverse proxy, or switch to the RavenDB adapter for horizontal scaling.
