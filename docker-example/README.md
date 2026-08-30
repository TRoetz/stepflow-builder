# StepFlow docker example

Runs the full stack as one Docker Compose demo: the visual builder UI (nginx), the standalone dynamic API host, and the StepFlow backend with its sample data. No seed job — the tracked `StepFunctionsApp/stepflow_data.db` already contains flows, EAV rows, and a published "Orders API".

## Architecture

```mermaid
flowchart LR
    B[Browser] -->|"/ (SPA)"| FE["frontend :80<br/>nginx + built SPA"]
    FE -->|"/api/*"| BE["backend :5001<br/>StepFunctionsApp (.NET 10)"]
    FE -->|"/mcp (SSE)"| BE
    H[Host tools] -.->|":5002 direct"| DAH["dynamic-api :5002<br/>DynamicApiHost (.NET 10)"]
    DAH -->|"catalog poll every 30 s<br/>GET api/dynamic/apis?published=true"| BE
```

## Quick start

From the repo root:

```powershell
docker compose -f docker-example/docker-compose.yml up --build
```

Then open `http://localhost:8080/`.

## URLs

| URL | What it is |
|---|---|
| `http://localhost:8080/` | Builder UI (nginx; `/api` and `/mcp` proxied to the backend) |
| `http://localhost:5001/api/health` | Backend direct — returns `"healthy"` |
| `http://localhost:5002/api/dynamic/orders` | Published Orders API via DynamicApiHost (EAV domain `OrderApproval`; GET lists rows, POST creates one) |

The dynamic API requires the demo bearer token that ships in the sample DB:

```powershell
curl -H "Authorization: Bearer 7336d1c033212a69afeba44a99bbdbb5" http://localhost:5002/api/dynamic/orders/
```

## Verification curls

```powershell
# UI HTML contains "StepFlow"
curl -s http://localhost:8080/ | Select-String StepFlow

# Proxied health returns "healthy"
curl -s http://localhost:8080/api/health

# Dynamic API eav GET with bearer token
curl -H "Authorization: Bearer 7336d1c033212a69afeba44a99bbdbb5" http://localhost:5002/api/dynamic/orders/
```

## Notes

- **Port overrides**: the backend binds `localhost` per the `DynamicApi` section of its appsettings.json; compose sets `DynamicApi__ListenAddress="*"` so sibling containers can reach it. DynamicApiHost likewise gets `ASPNETCORE_URLS=http://+:5002` and `Engine__BaseUrl=http://backend:5001`.
- **Self-healing**: if the backend starts after dynamic-api, the catalog poll (every 30 s) picks up published APIs without a restart.
- **Teardown**: `docker compose -f docker-example/docker-compose.yml down`
