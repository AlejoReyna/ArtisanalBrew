# Migración de ArtisanalBrew a Atlantic.Net

Fecha de migración: 12 de agosto de 2026

Estado: aplicación publicada y operativa; activación de Resend pendiente

## Objetivo y decisiones

La infraestructura de ArtisanalBrew se migró a una instancia Compute de
Atlantic.Net para consolidar la aplicación, PostgreSQL y la experiencia de
pagos agénticos en un solo servidor administrable.

Decisiones tomadas:

- No conservar la infraestructura de Azure como entorno activo.
- Mantener la URL pública `https://cafe.alexisreyna.dev`.
- Ejecutar PostgreSQL localmente, dentro de Docker y sin publicar su puerto.
- Mantener el gateway MCP/x402 para pagos agénticos en Base Sepolia.
- Guardar los recibos PDF en disco local y enviarlos mediante Resend.
- Exponer únicamente SSH, HTTP y HTTPS; los demás servicios viven en la red
  privada de Docker.

## Infraestructura resultante

| Recurso | Configuración |
|---|---|
| Proveedor | Atlantic.Net |
| Plan | G3.4GB Compute |
| Sistema operativo | Ubuntu 26.04 LTS, 64 bits |
| CPU y memoria | 2 vCPU, 4 GB RAM y 2 GB de swap |
| Disco y transferencia | 80 GB SSD y 5 TB por periodo |
| IPv4 | `209.23.11.117` |
| Dominio | `cafe.alexisreyna.dev` |
| Entrada pública | Caddy en puertos 80 y 443 |
| Administración | SSH por llave pública |

La topología desplegada es:

```text
Internet
   |
   v
Caddy :80/:443 -- TLS automático
   |-- Web (.NET 10) :8080
   `-- Agent Gateway (Node 24) :4022
           |
           v
     PostgreSQL 16

Worker (.NET 10) ------> PostgreSQL 16
```

Caddy envía `/sse`, `/messages`, `/bazaar` y
`/.well-known/agent-card.json` al Agent Gateway. El resto del tráfico llega a
la aplicación Web. Los puertos internos no están publicados en el host.

## Proceso realizado

1. Se provisionó el servidor y se apuntó el registro DNS `A` de `cafe` a la
   IPv4 del servidor. No se publicó un registro `AAAA`.
2. Se instaló una llave Ed25519 para acceso administrativo y se creó el usuario
   operativo `artisanalbrew`.
3. Se deshabilitó el acceso SSH por contraseña, incluido el acceso de `root`
   por contraseña. Se configuraron UFW y fail2ban; UFW permite solamente
   `22/tcp`, `80/tcp` y `443/tcp`.
4. Se agregó swap de 2 GB y se instaló Docker desde su repositorio oficial.
5. Se desplegaron cinco servicios con Docker Compose: `postgres`, `web`,
   `worker`, `gateway` y `caddy`, todos con límites de CPU y memoria y política
   `unless-stopped`.
6. Se inicializó PostgreSQL 16 en almacenamiento persistente local y se
   aplicaron las migraciones de la aplicación.
7. Se configuró Caddy como proxy inverso. Después de propagarse DNS, Caddy
   obtuvo y renovará automáticamente el certificado TLS.
8. Se corrigió el uso de encabezados reenviados en ASP.NET para conservar el
   esquema HTTPS detrás del proxy.
9. Se construyó el Agent Gateway como imagen Node 24 de producción, ejecutada
   por un usuario no privilegiado. El reto x402 se devuelve como contenido MCP
   estructurado para que un cliente compatible pueda pagar y reintentar.
10. Se habilitaron comprobaciones locales cada cinco minutos y respaldos
    diarios de PostgreSQL y recibos, con siete días de retención local.
11. Se reemplazó el almacenamiento de recibos dependiente de Azure por archivos
    locales y se preparó el envío de correo por la API HTTPS de Resend.

## Layout operativo

| Ruta en el servidor | Propósito |
|---|---|
| `/opt/artisanalbrew/repo` | Checkout de la aplicación |
| `/opt/artisanalbrew/compose.yml` | Compose activo |
| `/opt/artisanalbrew/Caddyfile` | Configuración activa del proxy |
| `/opt/artisanalbrew/*.env` | Configuración secreta; propiedad de `root`, modo `0600` |
| `/opt/artisanalbrew/data/postgres` | Volumen persistente de PostgreSQL |
| `/opt/artisanalbrew/data/receipts` | Recibos PDF persistentes |
| `/opt/artisanalbrew/backups/postgres` | Dumps diarios de PostgreSQL |
| `/opt/artisanalbrew/backups/receipts` | Archivos diarios de recibos |
| `/opt/artisanalbrew/caddy_data` | Certificados y estado de Caddy |

Los archivos fuente para reproducir la configuración viven en
`deployments/atlantic`. Ningún secreto debe añadirse al repositorio.

## Operación habitual

Conexión:

```bash
ssh -i ~/.ssh/artisanalbrew-atlantic-codex artisanalbrew@209.23.11.117
```

Estado y logs:

```bash
sudo docker compose -f /opt/artisanalbrew/compose.yml ps
sudo docker compose -f /opt/artisanalbrew/compose.yml logs --tail=200 web
sudo docker compose -f /opt/artisanalbrew/compose.yml logs --tail=200 gateway
sudo journalctl -u artisanalbrew-healthcheck.service --since today
sudo journalctl -u artisanalbrew-backup.service --since today
```

Despliegue manual de una revisión aprobada:

```bash
cd /opt/artisanalbrew/repo
git pull --ff-only
sudo install -m 0644 deployments/atlantic/compose.yml /opt/artisanalbrew/compose.yml
sudo install -m 0644 deployments/atlantic/Caddyfile /opt/artisanalbrew/Caddyfile
sudo docker compose -f /opt/artisanalbrew/compose.yml build
sudo docker compose -f /opt/artisanalbrew/compose.yml up -d
sudo docker compose -f /opt/artisanalbrew/compose.yml ps
```

Validación pública mínima:

```bash
curl --fail --show-error https://cafe.alexisreyna.dev/health/ready
curl --fail --show-error https://cafe.alexisreyna.dev/.well-known/agent-card.json
curl --fail --show-error https://cafe.alexisreyna.dev/bazaar
```

Ejecutar un respaldo manual y revisar los timers:

```bash
sudo systemctl start artisanalbrew-backup.service
sudo systemctl status artisanalbrew-backup.service
systemctl list-timers 'artisanalbrew-*'
```

## Respaldos y recuperación

`artisanalbrew-backup.timer` se ejecuta diariamente a partir de las 03:15 UTC,
con un retraso aleatorio de hasta quince minutos. Produce un dump PostgreSQL en
formato custom y un archivo comprimido de recibos. Los archivos con más de siete
días se eliminan.

Para comprobar un dump sin reemplazar la base activa, se debe restaurar primero
en una base temporal:

```bash
sudo docker compose -f /opt/artisanalbrew/compose.yml exec -T postgres \
  createdb -U thiscafeteria this_cafeteria_restore
sudo docker compose -f /opt/artisanalbrew/compose.yml exec -T postgres \
  pg_restore -U thiscafeteria -d this_cafeteria_restore \
  < /opt/artisanalbrew/backups/postgres/ARCHIVO.dump
```

Los respaldos actuales permanecen en el mismo VPS. Protegen contra errores de
aplicación, pero no contra la pérdida completa del servidor. Antes de considerar
la recuperación terminada se necesita una copia cifrada fuera de Atlantic.Net y
una prueba periódica de restauración.

## Resend

La aplicación ya incluye el cliente de Resend, archivos adjuntos PDF e
idempotencia por pedido. La configuración activa vive en
`/opt/artisanalbrew/resend.env` y nunca debe copiarse al repositorio.

Para activar el envío:

1. Agregar y verificar `send.alexisreyna.dev` en Resend.
2. Publicar en DNS exactamente los registros SPF, DKIM y de verificación que
   entregue Resend.
3. Crear una API key restringida al envío desde ese dominio.
4. Escribir la key directamente en `Resend__ApiKey` dentro del servidor y
   conservar el archivo con modo `0600`.
5. Recrear `web` y enviar un recibo de prueba a una dirección controlada.
6. Revisar el evento en Resend y confirmar aceptación, entrega y ausencia de
   duplicados.

```bash
sudo docker compose -f /opt/artisanalbrew/compose.yml up -d --force-recreate web
sudo docker compose -f /opt/artisanalbrew/compose.yml logs --tail=200 web
```

Mientras `Resend__ApiKey` esté vacío, la aplicación falla explícitamente al
intentar enviar un recibo; no simula una entrega exitosa.

## Pagos agénticos

El gateway MCP está publicado y anuncia cuatro herramientas. Las herramientas
pagadas producen un reto x402 estructurado en Base Sepolia (`eip155:84532`) con
USDC de prueba. El reto de `create_brew_plan` se verificó con un precio de
`10000` unidades base (0.01 USDC).

El estado actual es deliberadamente limitado:

- x402 en Base Sepolia está habilitado para pruebas.
- La reconciliación Ethereum Sepolia del worker está habilitada.
- BSC y Solana siguen deshabilitados en el worker hasta configurar RPCs
  dedicados y confiables.
- La redención de sesiones ERC-4337 sigue deshabilitada hasta disponer de un
  bundler seguro y un firmante remoto; no se debe usar una llave privada en el
  proceso del gateway en producción.

## Estado y pendientes

Completado:

- Aplicación Web, Worker, PostgreSQL, Agent Gateway y Caddy en ejecución.
- DNS, HTTPS válido y redirección correcta detrás del proxy.
- PostgreSQL y recibos persistentes en el host.
- Retos MCP/x402 estructurados y prueba pública del gateway.
- Health check cada cinco minutos y respaldo diario local.
- Código y configuración preparados para Resend.

Pendiente antes de considerar la plataforma completamente operativa:

- Verificar el dominio en Resend, instalar la API key y probar una entrega real.
- Copiar los respaldos a almacenamiento externo cifrado y ensayar la
  restauración.
- Habilitar snapshots periódicos del servidor en Atlantic.Net.
- Persistir las llaves de ASP.NET Data Protection fuera del contenedor para no
  invalidar cookies tras recrearlo.
- Rotar la contraseña inicial de `root` si aún no se hizo. Aunque el login por
  contraseña está deshabilitado, toda credencial de bootstrap debe tratarse
  como temporal.
- Configurar RPCs de producción para BSC/Solana antes de habilitar su
  reconciliación.
- Proveer bundler seguro y firmante remoto antes de activar sesiones ERC-4337.
- Automatizar el despliegue; actualmente la actualización es manual.
- Atender las advertencias de dependencias detectadas por las pruebas,
  especialmente los avisos de seguridad en dependencias transitivas de tests.

## Rollback

Azure no se conserva como destino de rollback. Para una regresión de código,
se debe seleccionar el último commit conocido como estable, reconstruir las
imágenes y volver a validar salud y flujos críticos. Un cambio de esquema de
base de datos requiere su propio plan de reversión; no se debe restaurar un dump
sobre la base activa sin detener escrituras, conservar una copia nueva y probar
primero la restauración en una base temporal.

Ante pérdida total del host, el orden de recuperación es: servidor Ubuntu
nuevo, endurecimiento y Docker, restauración de secretos por un canal seguro,
restauración de PostgreSQL y recibos, despliegue de Compose/Caddy, cambio de DNS
y validación pública. Este escenario seguirá incompleto mientras los respaldos
solo existan en el mismo VPS.
