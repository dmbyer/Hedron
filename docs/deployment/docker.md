# Docker Migration Plan

> Produced by a research sub-agent. Reviewed and accepted for Hedron. Implement alongside or immediately after persistence-reform Stage A (which adds `Persistence:DatabasePath` and makes paths container-friendly).

---

## Summary

Containerises the Hedron .NET 8 MUD server. The migration is non-breaking: config defaults move to absolute `/app/data/…` paths and a single hardcoded port is wired to configuration. All data is in named Docker volumes; designers update YAML content without rebuilding the image.

---

## Pre-requisites — two code changes before shipping the Docker files

**1. Make the telnet port configurable.**
`Server/Sessions/TelnetServer.cs` currently hardcodes `Port = 4000`. Wire it to `IConfiguration["Telnet:Port"]` (default 4000) so `docker-compose.yml` can override it via environment variable. This is a one-line constructor change.

**2. Update `appsettings.json` to absolute paths.**
Container working directory is `/app`. Relative paths resolve from there but explicit absolute paths avoid surprises:

```json
{
  "Output": { "DefaultColor": true },
  "Persistence": {
    "FlushIntervalSeconds": 60,
    "DatabasePath": "/app/data/hedron.db"
  },
  "World": {
    "ContentDirectory": "/app/data/content/",
    "StartingRoomBlueprintId": "room.crossroads"
  },
  "Admin": { "PrivilegedNames": ["admin"] },
  "Heartbeat": { "IntervalMs": 2000 },
  "Telnet": { "Port": 4000 },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

Notes:
- `Persistence:DataDirectory` (old JSON entity directory) is removed when persistence-reform Stage A ships; `Persistence:DatabasePath` replaces it.
- `Server:Port` renamed to `Telnet:Port` to match the code fix above.

---

## Dockerfile

```dockerfile
# ─── Build stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

COPY Hedron.sln .
COPY Server/Server.csproj Server/
COPY Core/Core.csproj Core/
RUN dotnet restore Hedron.sln

COPY Server/ Server/
COPY Core/ Core/
RUN dotnet build Hedron.sln -c Release --no-restore
RUN dotnet publish Server/Server.csproj -c Release -o /publish --no-build

# ─── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app

# Safe PID-1 signal handling
RUN apk add --no-cache dumb-init

# Non-root user
RUN addgroup -g 1000 hedron && adduser -D -u 1000 -G hedron hedron

COPY --from=builder --chown=hedron:hedron /publish .

# Data directories (content is also volume-mounted; this ensures they exist)
RUN mkdir -p /app/data/content && chown -R hedron:hedron /app/data

USER hedron

EXPOSE 4000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD echo "ok" || exit 1

ENTRYPOINT ["/sbin/dumb-init", "--"]
CMD ["dotnet", "Server.dll"]
```

---

## docker-compose.yml

```yaml
services:
  hedron-server:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: hedron-server
    restart: unless-stopped
    ports:
      - "4000:4000"
    volumes:
      - hedron-content:/app/data/content
      - hedron-db:/app/data
    environment:
      DOTNET_ENVIRONMENT: Production
      Persistence__DatabasePath: /app/data/hedron.db
      World__ContentDirectory: /app/data/content/
      Telnet__Port: 4000
      Logging__LogLevel__Default: Information

volumes:
  hedron-content:
  hedron-db:
```

**Volume strategy:**
| Volume | Mount | Contents | Notes |
|---|---|---|---|
| `hedron-content` | `/app/data/content` | Designer YAML files | Updated without rebuilding; mount as host dir in dev |
| `hedron-db` | `/app/data` | `hedron.db` (SQLite) + any future files | Single volume for all runtime state |

The two-volume split lets you version-control and deploy `content` independently of runtime state.

---

## .dockerignore

```
.git
.gitignore
.vs
.vscode
bin
obj
*.user
docs
.github
.claude
data/
README.md
CLAUDE.md
```

---

## SQLite notes (applies when persistence-reform Stage A ships)

Enable WAL mode in `PersistenceSystem` immediately after opening the connection:

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "PRAGMA journal_mode = WAL;";
cmd.ExecuteNonQuery();
```

WAL improves concurrent read performance during flush cycles. The three files SQLite creates (`hedron.db`, `hedron.db-wal`, `hedron.db-shm`) are all in `/app/data/` — owned by the `hedron` user in the container, stored in the `hedron-db` volume on the host.

---

## Build and deploy commands

```bash
# Build image
docker build -t hedron-mud:latest .

# Start (first run creates volumes)
docker compose up -d

# View logs
docker compose logs -f hedron-server

# Stop (volumes persist)
docker compose down

# Destroy including volumes
docker compose down -v
```

### Seed content files on first run

```bash
# After docker compose up -d, copy content into the named volume via the running container
docker compose cp ./data/content/. hedron-server:/app/data/content/

# Restart to load the content
docker compose restart hedron-server
docker compose logs hedron-server
```

### Update YAML content without rebuilding

```bash
docker compose cp ./updated-rooms.yaml hedron-server:/app/data/content/rooms/
# Then trigger an in-game reload:
# telnet localhost 4000 → @reload
```

### Local dev: bind-mount your content directory

Create `docker-compose.override.yml` (do not commit):

```yaml
services:
  hedron-server:
    volumes:
      - ./data/content:/app/data/content   # host path; edit files, reload in-game
      - ./data:/app/data                   # local SQLite DB
```

---

## Open questions for the user

1. **Registry** — Docker Hub, GitHub Container Registry, or Azure Container Registry?
2. **Deployment target** — local only, cloud VM, or Kubernetes?
3. **Backup strategy** for `hedron-db` volume (entity state)?
4. **TLS** — reverse proxy (Nginx/Traefik) for future web-facing surfaces?

---

## Implementation checklist

- [ ] Fix `TelnetServer.cs`: read port from `IConfiguration["Telnet:Port"]`
- [ ] Update `appsettings.json`: absolute paths, `Telnet:Port`, `Persistence:DatabasePath`
- [ ] Add `Dockerfile` to repo root
- [ ] Add `docker-compose.yml` to repo root
- [ ] Add `.dockerignore` to repo root
- [ ] `docker build -t hedron-test . && docker compose up -d` — verify startup log
- [ ] `telnet localhost 4000` — verify connection
- [ ] Restart container — verify player character reloads
- [ ] Add Docker quickstart to `CLAUDE.md`
