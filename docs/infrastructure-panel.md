# Panel «Infraestructura» (navbar)

Botón «Infraestructura» en la navbar → panel lateral oscuro. Datos del lado servidor
(`Services/Monitoring`), resumen del mismo origen en `GET /api/infrastructure/summary`
(`Cache-Control: no-store`). El navegador nunca recibe la URL de Prometheus/Grafana, credenciales
ni nombres internos.

Diagramas de toda la infraestructura (VPS, Prometheus/Grafana, demo AKS):
[infraestructura-diagramas.md](infraestructura-diagramas.md).

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
| `Enabled` | `false` → el endpoint responde 404, el botón de la navbar y el panel no se renderizan y el servicio no hace ninguna consulta a Prometheus. |

Estados: **Sin configurar** (no hay fuente), **Sin conexión** (error/timeout), **Datos desactualizados**
(muestra vieja o métrica ausente), **Operativo** (todas las métricas leídas y recientes).

## VPS (Atlantic.Net)

Consultas fijas (Node Exporter + cAdvisor): CPU `node_cpu_seconds_total`, memoria `node_memory_*`,
contenedores `container_last_seen{name!=""}`.

El stack de monitoreo vive fuera de este repo, en `/opt/monitoring/compose.yml` (proyecto Compose
`monitoring`): `prometheus` (jobs `prometheus`, `node`, `cadvisor`), `node-exporter`, `cadvisor` y
`grafana`. Prometheus solo está en la red `monitoring` (nombre real `monitoring_monitoring`) y no
publica puertos; Grafana está en `monitoring` y en `artisanalbrew_default` y escucha en
`127.0.0.1:3000`. Por eso `deployments/atlantic/compose.yml` une `web` a `monitoring_monitoring`
(red `external`) además de `default`, y la dirección interna es **`http://prometheus:9090`**.

Una sola vez, en `/opt/artisanalbrew/web.env` (propiedad de `root`, 0600):

```
InfrastructurePanel__Prometheus__BaseUrl=http://prometheus:9090
InfrastructurePanel__Grafana__Url=https://grafana.alexisrs.dev
```

Grafana ya se publica por Caddy en `grafana.alexisrs.dev` (con login), y el panel enlaza esa dirección
(pestaña Grafana → «Abrir en Grafana»). Si algún día solo hay túnel, usa `Grafana__Private=true`
(«Acceso privado», sin enlace) y quita `Grafana__Url`.

Comprobación de red antes de desplegar: `sudo docker network inspect monitoring_monitoring` debe listar
`prometheus`; tras el despliegue, `web` debe resolver y alcanzar `prometheus:9090`:

```bash
sudo docker compose -f /opt/artisanalbrew/compose.yml exec -T web \
  bash -c 'getent hosts prometheus && exec 3<>/dev/tcp/prometheus/9090 && echo ok'   # sin curl/wget en la imagen
```

## AKS

Sin Prometheus por defecto (ver `deployments/aks/dashboard.md`). Si el entorno tiene Prometheus +
kube-state-metrics, definir `Prometheus__BaseUrl` y se mostrarán «Pods listos».
