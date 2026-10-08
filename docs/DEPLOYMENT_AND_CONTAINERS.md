# Deployment, Docker & Kubernetes Architecture Guide

This guide covers the containerization and cloud orchestration architecture for the .NET 10 Modular Monolith platform.

---

## 1. Docker Strategy Overview

The platform uses two optimized container definitions:

| Image | Dockerfile | Target Environment | Key Characteristics |
| :--- | :--- | :--- | :--- |
| **Development** | `Dockerfile.dev` | Local Workstations | Hot reload (`dotnet watch`), live source code volume mounts, fast feedback cycle |
| **Production** | `Dockerfile` | Staging, Production & Kubernetes | Multi-stage build, layer-cached restores, non-root user (`app`, UID 1654), self-contained EF Core migration bundles, healthchecks |

---

## 2. Local Development (`docker-compose.yml`)

Developers can choose between two workflows:

### Workflow A: Infrastructure-only (Fastest IDE experience)
Run PostgreSQL 17, Redis 7, and Mailpit in background containers, while running the API directly from your IDE:
```bash
docker compose up -d db redis mailpit
```
- PostgreSQL: `localhost:5432` (user: `postgres`, password: `postgres`, db: `platform_dev`)
- Redis: `localhost:6379`
- Mailpit Web Dashboard: `http://localhost:8025` (Catches all sent emails)
- Mailpit SMTP Port: `localhost:1025`

### Workflow B: Full Containerized Stack with Hot-Reload
Spins up infrastructure and the API container using `Dockerfile.dev`:
```bash
docker compose up --build
```
Any code change in your local editor triggers an immediate hot-reload inside the container without rebuilding the Docker image.

---

## 3. Production Docker Compose (`docker-compose.prod.yml`)

For self-hosted single-node or VM deployments:

```bash
# 1. Create your production environment file
cp .env.example .env.production
# (Edit .env.production with your production secrets)

# 2. Run the production stack
docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build
```

### Stack Components:
1. **`migrations`**: Transient runner container that applies all EF Core migration bundles across all 8 modules sequentially before the API starts.
2. **`api`**: Hardened production container running under non-root UID 1654 with memory limits (2GB limit) and liveness healthchecks.
3. **`db`**: Tuned PostgreSQL 17 container with optimized WAL, memory, and checkpoint parameters.
4. **`redis`**: Password-protected Redis 7 with Append-Only File (AOF) persistence and LRU eviction.

---

## 3b. Production Reverse Proxy with Traefik v3 (`docker-compose.traefik.yml`)

The platform requires an edge reverse proxy in front of Kestrel for SSL termination, request buffering, and WebSocket upgrades.

### Running Production with Traefik Edge Router
```bash
docker compose -f docker-compose.prod.yml -f docker-compose.traefik.yml --env-file .env.production up -d
```

### What Traefik Does Automatically:
- **Automatic Let's Encrypt TLS**: Obtains and renews SSL certificates for `api.esocs.org` automatically via ACME TLS-ALPN-01 challenges.
- **Port 80 to 443 Redirection**: Redirects plain HTTP requests to HTTPS.
- **WebSocket & SignalR Passthrough**: Streams real-time notifications with zero connection timeouts.
- **Security Middlewares**: Adds HSTS headers, X-Frame-Options (`DENY`), Content-Type nosniff, and buffers uploads up to 64MB for sermon recordings and CSV imports.


## 4. Kubernetes (k8s) Microservices Architecture

The platform provides a production-grade Kustomize-based Kubernetes configuration located in `k8s/`:

```
k8s/
├── base/
│   ├── namespace.yaml           # Dedicated 'platform' namespace
│   ├── configmap.yaml           # Application runtime settings
│   ├── secret.template.yaml     # Production credentials template
│   ├── pvc.yaml                 # Persistent storage for uploads
│   ├── migration-job.yaml       # Pre-deployment migration runner Job
│   ├── api-deployment.yaml      # High-availability web API Deployment
│   ├── worker-deployment.yaml   # Dedicated background worker Deployment
│   ├── api-service.yaml         # ClusterIP internal routing
│   ├── ingress.yaml             # Ingress with TLS & SignalR WebSockets
│   ├── hpa.yaml                 # Horizontal Pod Autoscaler (2 to 10 pods)
│   ├── pdb.yaml                 # Pod Disruption Budget (minAvailable: 1)
│   ├── network-policy.yaml      # Zero-trust network segmentation
│   └── kustomization.yaml       # Base assembly
└── overlays/
    ├── staging/                 # Staging cluster configuration
    └── production/              # Production cluster configuration (3+ replicas)
```

### Pre-Deployment Migration Strategy
Before rolling out updated API pods, apply the database migration Job:
```bash
kubectl apply -f k8s/base/migration-job.yaml
kubectl wait --for=condition=complete job/platform-db-migrations -n platform --timeout=300s
```

### Deploying with Kustomize
```bash
# Deploy to staging
kubectl apply -k k8s/overlays/staging

# Deploy to production
kubectl apply -k k8s/overlays/production
```

### Probes & Zero-Downtime Guarantee
- **Startup Probe**: Probes `GET /health/live` to allow cold start before liveness checks kick in.
- **Liveness Probe**: Probes `GET /health/live` every 15 seconds to restart unresponsive pods.
- **Readiness Probe**: Probes `GET /health/ready` every 10 seconds to ensure database and Redis dependencies are reachable before receiving user traffic.
- **Rolling Update**: Configured with `maxSurge: 1` and `maxUnavailable: 0` so new pods are fully ready before old pods terminate.

