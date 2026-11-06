# Panel «Infraestructura» (navbar)

Botón «Infraestructura» en la navbar → panel lateral oscuro. Datos del lado servidor
(`Services/Monitoring`), resumen del mismo origen en `GET /api/infrastructure/summary`
(`Cache-Control: no-store`). El navegador nunca recibe la URL de Prometheus/Grafana, credenciales
ni nombres internos.

## Configuración (`InfrastructurePanel__*`)

| Variable | Significado |
|---|---|
| `Provider` | `atlantic` → «Atlantic.Net · VPS / Docker Compose»; `azure` → «Azure · AKS». Vacío → «Sin configurar». |
| `Environment`, `Release` | Etiqueta de entorno y commit/tag (si falta `Release` se usa el commit del ensamblado, si existe). |
| `Prometheus__BaseUrl` | URL **interna** (`http://prometheus:9090`). Vacía → «Sin configurar». Consultas fijas, tiempo límite `TimeoutSeconds` (2 s), caché `CacheSeconds` (15 s). |
| `Prometheus__StaleAfterSeconds` | Edad máxima de la muestra para decir «Operativo» (120 s). Mayor → «Datos desactualizados». |
| `Prometheus__Namespace` | Solo AKS: namespace para las consultas de kube-state-metrics. |
| `Grafana__Url` | Dashboard autorizado (solo `https`, sin credenciales en la URL). |
| `Grafana__Private` | `true` si solo hay túnel/port-forward → «Acceso privado», sin enlace. |
| `SampleData` | Solo en `Development`: métricas de ejemplo marcadas «Datos de ejemplo». |
| `Enabled` | `false` → el endpoint responde 404. |

Estados: **Sin configurar** (no hay fuente), **Sin conexión** (error/timeout), **Datos desactualizados**
(muestra vieja o métrica ausente), **Operativo** (todas las métricas leídas y recientes).

## VPS (Atlantic.Net)

Consultas fijas (Node Exporter + cAdvisor): CPU `node_cpu_seconds_total`, memoria `node_memory_*`,
contenedores `container_last_seen{name!=""}`. Requisito: el contenedor `web` debe alcanzar a
Prometheus por una red interna de Docker (p. ej. unir `web` a la red del stack de monitoreo y usar
`http://prometheus:9090`) **sin publicar puertos** en el host. El repo no contiene el stack de
monitoreo del VPS; hay que verificar en el servidor el nombre del servicio, la red y los *jobs*.

## AKS

Sin Prometheus por defecto (ver `deployments/aks/dashboard.md`). Si el entorno tiene Prometheus +
kube-state-metrics, definir `Prometheus__BaseUrl` y se mostrarán «Pods listos».
