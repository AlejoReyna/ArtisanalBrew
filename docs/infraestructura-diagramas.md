# Infraestructura de ArtisanalBrew: diagramas

Qué corre dónde, cómo se instala y configura, y cómo se conectan Prometheus y Grafana.

| Entorno | Tecnología | Estado |
|---|---|---|
| **Producción** `cafe.alexisrs.dev` | VPS Atlantic.Net con **Docker Compose** (no Kubernetes) | en producción |
| **Demo AKS** | Kubernetes en Azure (AKS) con Terraform y Azure DevOps | demo aislada; su código vive en la rama `k8s` y **aún no está en `main`** (`deployments/aks/`, `infra/terraform/`) |
| Azure Container Apps | Bicep en `infra/` | desactivado salvo `ENABLE_AZURE_DEPLOY=true` |

**Ansible no se usa en este repositorio.** El VPS se aprovisionó a mano y se actualiza con Compose
(sección 5); la demo AKS usa Terraform. No hay playbooks ni inventarios.

Índice: [1. Producción](#1-producción-atlantic-vps) · [2. Prometheus y Grafana](#2-prometheus-y-grafana-en-el-vps) ·
[3. Instalación y configuración del VPS](#3-instalación-y-configuración-del-vps) ·
[4. Kubernetes (demo AKS)](#4-kubernetes-demo-aks) · [5. Ansible](#5-ansible)

## 1. Producción (Atlantic VPS)

```mermaid
flowchart LR
  User["Visitantes y agentes MCP"] -->|HTTPS 443| Caddy
  subgraph VPS["VPS Atlantic.Net, UFW 22, 80, 443"]
    subgraph Net["red artisanalbrew_default"]
      Caddy["caddy<br/>TLS automático"]
      Web["web: ASP.NET + Blazor<br/>:8080"]
      Gateway["gateway: Node 24, x402 y MCP<br/>:4022"]
      Worker["worker: reconciliación"]
      PG[("postgres 16<br/>volumen local")]
    end
    Other["otros sitios del mismo Caddy<br/>(catalog, alice-storage, mcp, grafana...)"]
    Files[("recibos y claves de Data Protection<br/>/opt/artisanalbrew/data")]
    Backup["respaldos diarios<br/>PostgreSQL y recibos, 7 días"]
  end
  Caddy -->|/sse, /messages, /bazaar, agent-card| Gateway
  Caddy -->|resto| Web
  Caddy --> Other
  Gateway -->|API interna| Web
  Web --> PG
  Worker --> PG
  Gateway --> PG
  Web --> Files
  PG --> Backup
  Files --> Backup
```

- Solo Caddy publica puertos; `web` y `gateway` usan `expose` dentro de la red de Compose.
- Cada servicio tiene límites de CPU y memoria y `restart: unless-stopped`.
- El `Caddyfile` **activo** del servidor sirve además sitios de otros proyectos. No se instala desde el
  repo sobre el activo (el del repo no los contiene).

## 2. Prometheus y Grafana en el VPS

El stack de monitoreo está **fuera de este repo**, en `/opt/monitoring` (proyecto Compose `monitoring`).

```mermaid
flowchart LR
  subgraph Mon["red monitoring_monitoring"]
    NE["node-exporter<br/>CPU y memoria del host"]
    CA["cadvisor<br/>contenedores Docker"]
    Prom["prometheus :9090<br/>scrape 30 s, 15 d / 5 GB<br/>sin puertos publicados"]
    Graf["grafana :3000<br/>127.0.0.1:3000"]
  end
  subgraph App["red artisanalbrew_default"]
    Web["web .NET"]
    Caddy["caddy"]
  end
  NE -->|job node| Prom
  CA -->|job cadvisor| Prom
  Prom -->|job prometheus| Prom
  Prom -->|datasource| Graf
  Graf -.->|también en artisanalbrew_default| Caddy
  Caddy -->|grafana.alexisrs.dev, con login| Graf
  Web -->|"únete a monitoring_monitoring<br/>GET /api/v1/query"| Prom
```

- **Prometheus** solo está en `monitoring_monitoring` y no publica puertos. **Grafana** está en esa red y en
  `artisanalbrew_default`, escucha en `127.0.0.1:3000` y Caddy la publica en `grafana.alexisrs.dev` con login.
- **`web` se une a `monitoring_monitoring`** (en `deployments/atlantic/compose.yml`, red `external`) además de
  `default`, así llega a `http://prometheus:9090`. Esa red también contiene node-exporter, cAdvisor y Grafana.

### Panel «Infraestructura» de la web

```mermaid
sequenceDiagram
  participant B as Navegador
  participant W as web (Blazor, servidor)
  participant P as Prometheus
  B->>W: abre el panel (circuito Blazor)
  W->>W: InfrastructurePanel__Enabled? si no, no hace nada
  W->>P: 4 consultas fijas: CPU, memoria, contenedores, edad de la muestra
  P-->>W: valores
  W-->>B: resumen agregado, sin direcciones internas
  Note over B,W: también GET /api/infrastructure/summary, no-store
```

Si Prometheus no responde, el panel muestra «Sin conexión»; el sitio y el carrito no se ven afectados.
Con `InfrastructurePanel__Enabled=false`, el botón y el panel no se renderizan y no hay consultas.

## 3. Instalación y configuración del VPS

Disposición en el servidor:

| Ruta | Propósito |
|---|---|
| `/opt/artisanalbrew/repo` | checkout de la aplicación |
| `/opt/artisanalbrew/compose.yml` | Compose activo (copia de `deployments/atlantic/compose.yml`) |
| `/opt/artisanalbrew/Caddyfile` | proxy activo (no se sobrescribe desde el repo) |
| `/opt/artisanalbrew/*.env` | secretos, propiedad de `root`, 0600 |
| `/opt/artisanalbrew/data/{postgres,receipts,dataprotection}` | volúmenes persistentes |
| `/opt/monitoring/` | stack de monitoreo (Prometheus, node-exporter, cAdvisor, Grafana) |

```mermaid
flowchart TB
  A["1. Servidor Ubuntu, DNS A, usuario operativo,<br/>SSH solo con llave, UFW, fail2ban, swap, Docker"] --> B["2. Archivos *.env con secretos<br/>/opt/artisanalbrew"]
  B --> C["3. git pull del repo<br/>en /opt/artisanalbrew/repo"]
  C --> D["4. install compose.yml<br/>(Caddyfile solo si se revisa a mano)"]
  D --> E["5. docker compose build"]
  E --> F["6. docker compose up -d<br/>con ARTISANALBREW_RELEASE"]
  F --> G["7. validar /health/ready,<br/>agent-card y el panel"]
```

Despliegue de una revisión aprobada (ver también `docs/atlantic-migration.md`):

```bash
cd /opt/artisanalbrew/repo && git pull --ff-only
sudo docker network inspect monitoring_monitoring >/dev/null
sudo install -m 0644 deployments/atlantic/compose.yml /opt/artisanalbrew/compose.yml
sudo docker compose -f /opt/artisanalbrew/compose.yml build web
sudo env ARTISANALBREW_RELEASE="$(git rev-parse --short=12 HEAD)" \
  docker compose -f /opt/artisanalbrew/compose.yml up -d web
```

Configuración del panel, en `/opt/artisanalbrew/web.env`:

| Variable | Valor en producción |
|---|---|
| `InfrastructurePanel__Prometheus__BaseUrl` | `http://prometheus:9090` |
| `InfrastructurePanel__Grafana__Url` | `https://grafana.alexisrs.dev` (o `Grafana__Private=true` sin enlace) |

Las demás (`Provider=atlantic`, `Environment=production`, `Release`) las fija `compose.yml`. Referencia
completa en [infrastructure-panel.md](infrastructure-panel.md).

## 4. Kubernetes (demo AKS)

> Esta parte describe la rama `k8s` (`deployments/aks/README.md`, `infra/terraform/`,
> `azure-pipelines.yml`), todavía sin fusionar a `main`. Es una **demo aislada**, no producción.

### 4.1 Topología

```mermaid
flowchart LR
  Operator["Operador: bootstrap e infraestructura"] --> Infra["Terraform infrastructure.tfstate"]
  Pipeline["Azure Pipelines: Test, Build, Deploy"] --> ACR["ACR existente"]
  Pipeline --> App["Terraform application.tfstate"]
  subgraph AKS["AKS aks-devopslab-lab, namespace artisanalbrew-demo"]
    Job["Job: migraciones EF"]
    Web["web: 1 réplica"]
    Worker["worker"]
    Gateway["gateway"]
    Svc["Services ClusterIP"]
  end
  subgraph Azure["Recursos Azure de la demo"]
    PG[("PostgreSQL Flexible")]
    SB["Service Bus"]
    Blob["Blob: recibos y claves"]
    MI["Identidades: workload y pipeline<br/>Workload Identity federada"]
  end
  Infra --> Azure
  App --> AKS
  ACR -->|AcrPull del kubelet| AKS
  Job --> PG
  Web --> PG
  Worker --> PG
  Gateway --> PG
  Web --> Blob
  Web --> SB
  Worker --> SB
  Gateway -->|HTTPS| Facilitator["Facilitador x402 existente"]
  Gateway --> Web
  Web --- Svc
```

- Dos estados de Terraform en contenedores Blob separados: `infrastructure.tfstate` (identidades, PostgreSQL, Service Bus,
  contenedores Blob, RBAC) y `application.tfstate` (namespace, ConfigMaps, Job de migración, Deployments y Services).
- Los Deployments usan `strategy: Recreate` (las sesiones de Blazor y MCP y la reconciliación del worker son locales al proceso).
- AKS, ACR y la cuenta de estado se **reutilizan** con `data sources`; no se importan ni se recrean.

### 4.2 Pipeline

```mermaid
sequenceDiagram
  participant Op as Operador
  participant AzDO as Azure Pipelines
  participant ACR as ACR
  participant K as AKS
  Op->>AzDO: ejecutar con deploy = true (opt-in)
  AzDO->>AzDO: Test: suites .NET, gateway y validate de Terraform
  AzDO->>ACR: Build: imágenes web, worker, gateway y migraciones
  AzDO->>K: Deploy: terraform plan y apply de application
  K->>K: Job de migraciones, rollout y pruebas de humo reales
```

### 4.3 Instalación (resumen del runbook)

Orden de `deployments/aks/README.md`: inventario y herramientas → backend remoto (dos contenedores de
estado, con permisos de datos Blob para el operador) → `infra/terraform/infrastructure` → credenciales de
kubectl y namespace → secretos (`scripts/aks/`) → Azure DevOps (service connection federada, environment,
variable group) → primera ejecución → verificación, pausa y eliminación.

```bash
terraform -chdir=infra/terraform/infrastructure init -backend-config=backend.hcl   # backend.hcl desde el .example
```

Acceso a la web de la demo: `kubectl -n artisanalbrew-demo port-forward svc/web 8080:<puerto>` (el comando exacto
sale de la salida `web_access` de Terraform).

### 4.4 Monitoreo en AKS

**No hay Prometheus ni Grafana en AKS** (solo metrics-server y componentes de sistema). Con
`InfrastructurePanel__Provider=azure` el panel muestra «Azure · AKS» pero Prometheus aparece «Sin configurar».
Para mostrar «Pods listos» hace falta una fuente propia (Prometheus + kube-state-metrics, o Azure Managed Prometheus)
y fijar `InfrastructurePanel__Prometheus__BaseUrl` y `__Namespace`. Mientras tanto, las métricas de plataforma se
ven en Azure Portal → Kubernetes services → Monitoring → Metrics (`deployments/aks/dashboard.md`).

## 5. Ansible

No existe. Para quien lo busque, esto es lo que cubre cada necesidad hoy:

| Necesidad | Herramienta actual |
|---|---|
| Aprovisionar el VPS | pasos manuales documentados en `docs/atlantic-migration.md` |
| Configurar y desplegar en el VPS | Docker Compose + `install` de `compose.yml` |
| Infraestructura de la demo | Terraform (`infra/terraform/*`) |
| CI/CD de la demo | Azure Pipelines (`azure-pipelines.yml`) |
