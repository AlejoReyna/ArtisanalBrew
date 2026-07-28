 Auditoría operativa — Inverater Canary EC2 (i-0f5c502e5b406987c)

**Fecha de auditoría:** 2026-07-24
**Modo:** Solo lectura (read-only). Ningún servicio, contenedor, archivo, base de datos o recurso de AWS fue modificado, reiniciado, detenido o recreado durante esta auditoría.
**Instancia:** esta auditoría se ejecutó directamente dentro de una sesión de Claude Code corriendo *en* la propia instancia Canary (confirmado por metadata IMDSv2 y por la presencia de `/home/deploy/ruby-backend` y `/srv/go-api`).

Clasificación usada en todo el documento:
- **CONFIRMADO**: verificado con evidencia directa (comando o archivo citado).
- **INFERIDO**: deducido de evidencia indirecta consistente, sin verificación 100% directa.
- **NO VERIFICABLE DESDE EC2**: requiere acceso a la consola/API de AWS (Security Groups, IAM policy JSON, Route53, CloudFront) no disponible con el rol IAM adjunto ni con las herramientas instaladas en esta instancia.

---

## 1. Resumen ejecutivo

El servidor Canary de Inverater (`i-0f5c502e5b406987c`, t2.micro, us-east-1e) aloja en un solo host: el backend Ruby on Rails (Puma), el backend Go, y una base de datos PostgreSQL/PostGIS en Docker que sirve a ambos backends. NGINX enruta por dominio: `api-test.inverater.com` → Rails (socket Unix de Puma) y `api-test-v2.inverater.com` → Go (`127.0.0.1:8000`). WordPress **no** corre en esta instancia. **CONFIRMADO**

Se encontraron varios hallazgos de severidad alta que afectan tanto la fiabilidad como la seguridad de Canary:

1. **El binario en ejecución del backend Go fue borrado del disco** (`/proc/2298/exe -> /srv/go-api/release/app (deleted)`) tras un despliegue manual fuera del flujo estándar de CodeDeploy. El proceso sigue vivo solo porque Linux mantiene el inodo abierto; si el proceso muere o el servicio se reinicia, **Go dejará de arrancar** (`ExecStart` apunta a un symlink roto). **CONFIRMADO**
2. **El hook de despliegue de Rails exporta `RAILS_ENV=test` en despliegues de canary**, corre `bundle exec rake db:migrate` bajo ese entorno, y el último despliegue registrado (2026-04-15 18:08) **falló** con `LoadError: cannot load such file -- stripe_mock` porque el grupo `test` de gemas fue excluido del `bundle install`. Las migraciones **no se aplicaron** en el último despliegue. **CONFIRMADO** (log de CodeDeploy)
3. Pese a lo anterior, el servicio systemd real (`puma_ruby_backend.service`) arranca Puma con **`RAILS_ENV=canary`** (vía `/home/deploy/.rails_env`), no `test`. Hay una discrepancia real entre el entorno usado durante el hook de despliegue (test) y el entorno de ejecución real (canary). **CONFIRMADO**
4. El crontab de `whenever` **acumula ~12 bloques duplicados** de tareas desde despliegues anteriores (nunca se limpian porque cada bloque referencia una ruta de release distinta). Esto provoca que tareas como `SendPaymentRemindersJob` se ejecuten **~10 veces por día**, algunas con `-e production` — un entorno que **no existe** en `database.yml` (solo hay `default`, `canary`, `test`). Confirmado en `/var/log/syslog` de hoy mismo. **CONFIRMADO**
5. PostgreSQL (5432) y Redis (6379) del contenedor Docker están **publicados en `0.0.0.0`** (todas las interfaces), sin firewall local activo (`ufw` inactivo). La exposición real a Internet depende del Security Group de AWS, que no se pudo verificar desde esta instancia. **CONFIRMADO (local) / NO VERIFICABLE DESDE EC2 (alcance real por Internet)**
6. El usuario `deploy` tiene **sudo `(ALL:ALL) ALL`** además de reglas `NOPASSWD` sobre `systemctl` y `docker` sin restricción de subcomandos — equivalente a acceso root sin restricciones desde esa cuenta. **CONFIRMADO**
7. Archivos de configuración con secretos (`application.yml`, `database.yml`, `.rails_env`, `.cron_env`) son **legibles por cualquier usuario del sistema** (permisos 644/664). El archivo `.env` de Go (`/srv/go-api/shared/.env`) sí está correctamente restringido a 600. **CONFIRMADO**
8. No existe backup automatizado de PostgreSQL: el único dump encontrado es de **2024-11-07** (más de 20 meses de antigüedad) y el timer `pg_dump@.timer` está `disabled`. No hay evidencia de un procedimiento de restauración probado. **CONFIRMADO**
9. CORS en Rails usa `origins '*'` de forma **incondicional** (no solo en desarrollo, pese al comentario en el código) exponiendo headers sensibles (`Authorization`, `HMACSecret`, etc.) a cualquier origen. **CONFIRMADO**
10. El log de Rails (`canary.log`) pesa **1.4 GB sin rotación configurada**, y el disco raíz está al **82% de uso** — riesgo de llenado de disco en una instancia con solo 30 GB. **CONFIRMADO**

No se realizó ninguna acción de escritura, reinicio, migración, ni se expusieron contraseñas, tokens, claves privadas o cookies reales durante esta auditoría.

---

## 2. Diagrama textual del tráfico

```
Internet
  │
  ├── DNS: api-test.inverater.com     → IP pública 75.101.239.149 (esta instancia)     CONFIRMADO
  ├── DNS: api-test-v2.inverater.com  → IP pública 75.101.239.149 (esta instancia)     CONFIRMADO
  └── DNS: test.inverater.com         → CloudFront (d2zr10hfvxvrke.cloudfront.net)     CONFIRMADO (frontend/S3, fuera de esta instancia)

EC2 Canary (i-0f5c502e5b406987c, 172.31.52.24 / 75.101.239.149)
  │
  ├── NGINX :80/:443 (TLS Let's Encrypt)
  │     ├── server_name api-test.inverater.com
  │     │      └── proxy_pass → unix:/home/deploy/current/tmp/sockets/puma.sock
  │     │             └── puma_ruby_backend.service (Ruby on Rails 7.0.8.7, Ruby 3.1.3, RAILS_ENV=canary)
  │     │
  │     └── server_name api-test-v2.inverater.com
  │            └── proxy_pass → 127.0.0.1:8000
  │                   └── go-api.service (binario Go, ejecutable BORRADO del disco — ver hallazgos)
  │
  ├── Puerto 5432/tcp (0.0.0.0) → docker-proxy → contenedor inverater-postgres (postgis/postgis:14-3.3)
  ├── Puerto 6379/tcp (0.0.0.0) → docker-proxy → contenedor inverater-redis (redis)
  ├── Puertos 9292/9293 (puma.socket, legado) → puma.service (FAILED, inactivo desde 2026-04-15)
  └── Puerto 22/tcp → sshd

Ambos backends (Rails vía database.yml host 0.0.0.0:5432, Go vía DATABASE_URL no expuesto)
conectan al mismo Postgres/PostGIS local en Docker.                                    INFERIDO / CONFIRMADO parcial

Integraciones externas salientes (confirmadas por código/conexiones activas):
  Rails → Stripe (api.stripe.com, conexión activa observada), Mailjet, Intercom (api.intercom.io),
          Truora (api.account/connect/identity/validations.truora.com), STP/BARTeC (stp.inverater.com)
  Go    → Stripe, Mailjet (in-v3.mailjet.com), BARTeC (mismo backend, con errores actuales)
```

---

## 3. Inventario del host

| Campo | Valor | Estado |
|---|---|---|
| OS | Ubuntu 22.04.1 LTS (jammy) | CONFIRMADO — `/etc/os-release` |
| Kernel | Linux 6.8.0-1051-aws x86_64 | CONFIRMADO — `uname -a` |
| Hostname | ip-172-31-52-24 | CONFIRMADO |
| Uptime | 100 días, 1h11m (arriba desde ~2026-04-15) | CONFIRMADO — `uptime` |
| Instance ID | i-0f5c502e5b406987c | CONFIRMADO — IMDSv2 |
| Instance type | t2.micro | CONFIRMADO — IMDSv2 |
| Región / AZ | us-east-1 / us-east-1e | CONFIRMADO — IMDSv2 |
| AMI ID | ami-0557a15b87f6559cf | CONFIRMADO — IMDSv2 |
| Security Group (nombre local) | "Rails Web Server" | CONFIRMADO — IMDSv2 (nombre solamente; reglas no verificables sin AWS CLI) |
| IP privada | 172.31.52.24 | CONFIRMADO |
| IP pública | 75.101.239.149 | CONFIRMADO |
| Rol IAM adjunto | `EC2ReadBuckets` (solo nombre, sin solicitar credenciales) | CONFIRMADO — IMDSv2 `iam/security-credentials/` |
| Memoria | 957 MiB total, ~525 MiB usados, swap 2 GiB (604 MiB en uso) | CONFIRMADO — `free -h` |
| Disco raíz | 29 GB, 82% usado (5.5 GB libres) | CONFIRMADO — `df -hT` |
| Cuentas de login | root, ubuntu, deploy, postgres, godeploy | CONFIRMADO — `/etc/passwd` |

**Riesgo de capacidad:** instancia t2.micro (1 vCPU, ~1 GB RAM) corriendo NGINX + Rails + Go + Docker (Postgres+Redis) simultáneamente, con swap ya al 30% de uso y disco al 82%. Margen operativo bajo. **CONFIRMADO/INFERIDO**

---

## 4. Entrada DNS/TLS/reverse proxy

- **NGINX** 1.18.0, activo (`systemctl status nginx` → active/running desde 2026-07-24 06:33). CONFIRMADO
- Sitios habilitados (`/etc/nginx/sites-enabled/`): `api-test.inverater.com.conf`, `api-test-v2.inverater.com.conf`, `default`. CONFIRMADO

### Mapeo exacto de upstream

| Dominio | Puerto | Destino | Backend |
|---|---|---|---|
| `api-test.inverater.com` | 443 (TLS) → 80 (redirect 301) | `unix:///home/deploy/current/tmp/sockets/puma.sock` | Rails (Puma) |
| `api-test-v2.inverater.com` | 443 (TLS) → 80 (redirect 301) | `127.0.0.1:8000` | Go |

CONFIRMADO — `/etc/nginx/sites-available/api-test.inverater.com.conf`, `api-test-v2.inverater.com.conf`

No hay configuración de WordPress en NGINX. CONFIRMADO

### TLS (Let's Encrypt / Certbot vía snap)

| Dominio | Emisor | Vigencia | SAN |
|---|---|---|---|
| api-test.inverater.com | Let's Encrypt (CN=YE1) | 2026-06-15 → 2026-09-13 | api-test.inverater.com |
| api-test-v2.inverater.com | Let's Encrypt (CN=YE2) | 2026-06-14 → 2026-09-12 | api-test-v2.inverater.com |

CONFIRMADO — `openssl x509 -noout -issuer -subject -dates`. Renovación automática vía `snap.certbot.renew.timer` (próxima ejecución 2026-07-25 03:23 UTC). CONFIRMADO. No se expusieron claves privadas.

### Límites y cabeceras

- `api-test.inverater.com`: `client_max_body_size 100M`; sin soporte explícito de WebSocket (no `Upgrade`/`Connection`); forwarding estándar (`X-Forwarded-For`, `X-Forwarded-Proto`, `Host`). CONFIRMADO
- `api-test-v2.inverater.com`: `client_max_body_size 50M`; **sí** soporta WebSocket (`Upgrade`/`Connection: keep-alive`); mismo forwarding. CONFIRMADO
- Timeouts: no hay `proxy_read_timeout`/`proxy_connect_timeout` personalizados en `nginx.conf` → se usan los valores por defecto de NGINX (60s). CONFIRMADO

### Firewall local

- `ufw`: **inactive**. CONFIRMADO — `ufw status verbose`
- `iptables`: política `INPUT ACCEPT` sin reglas restrictivas de entrada; el chain `DOCKER` acepta explícitamente tráfico hacia los contenedores en los puertos 6379 y 5432. CONFIRMADO — `iptables -L -n`
- Conclusión: **no hay ningún control a nivel de SO** que impida el acceso externo a 5432/6379 si el Security Group de AWS lo permitiera. El comportamiento real del Security Group **no se verificó** (sin AWS CLI ni credenciales de rol solicitadas). NO VERIFICABLE DESDE EC2

### Puertos adicionales expuestos

- `9292`/`9293` (TCP, `0.0.0.0`): socket legado `puma.socket`, activado por systemd socket-activation, pero el servicio que dispara (`puma.service`) está **failed** desde 2026-04-15. Mientras el socket sigue en estado LISTEN, una conexión entrante fallaría en activar el servicio. CONFIRMADO

---

## 5. Backend Ruby

- **Unidad systemd activa:** `puma_ruby_backend.service` — `active (running)` desde 2026-04-15 18:08:52 (PID 2981, Puma 5.6.9, Ruby 3.1.3-p185). CONFIRMADO
- **Unidad legada `puma.service`**: existe, está **failed** (exit-code) desde 2026-04-15 17:50:14, tras 8 reintentos de arranque ("Start request repeated too quickly"). Ambas unidades declaran `Requires=puma.socket`, lo cual es una configuración duplicada/conflictiva que debería limpiarse. CONFIRMADO
- **Usuario/grupo:** `deploy`. **WorkingDirectory:** `/home/deploy/current`. **ExecStart:** `bundle exec puma -C .../config/puma.rb .../config.ru`. **Restart:** `always`. CONFIRMADO — `systemctl cat puma_ruby_backend.service`

### Estructura de directorios

- `/home/deploy/current` → symlink → `/home/deploy/ruby-backend` (no apunta a `releases/<timestamp>`). CONFIRMADO
- `/home/deploy/ruby-backend` **no es un repositorio git** (`fatal: not a git repository`) — el código se despliega como copia plana vía CodeDeploy (`appspec.yml`: `source: /`, `destination: /home/deploy/ruby-backend`), no como checkout de git. CONFIRMADO
- `/home/deploy/releases/` contiene 6 releases históricos con timestamps de Capistrano (últimos: `20260313191723`, `20260313195913`); ninguno corresponde al despliegue actual (2026-04-15), lo que confirma la migración de Capistrano → CodeDeploy (ver sección 10). CONFIRMADO
- `/home/deploy/shared/config/` contiene `application.yml`, `database.yml`, `master.key`, `credentials/{canary,test}.key`. CONFIRMADO
- Repositorio bare en `/home/deploy/repo` (remoto `git@bitbucket.org:inverater-frontend/inverater-backend.git`), con ~65 ramas incluyendo `development`, `master`, `staging`, `canary`-related feature branches. CONFIRMADO

### RAILS_ENV efectivo — hallazgo central

- El servicio systemd carga `EnvironmentFile=/home/deploy/.rails_env`, cuyo contenido es `RAILS_ENV=canary` / `RAILS_MAX_THREADS=2`. El log de arranque de Puma confirma `* Environment: canary`. **CONFIRMADO** → **Canary corre efectivamente con `RAILS_ENV=canary`, NO con `test` ni `production`.**
- Sin embargo, el script de despliegue (`scripts/codedeploy/start_server.sh`, visto en el paquete de CodeDeploy más reciente) hace:
  ```
  if [[ "$DEPLOYMENT_GROUP_NAME" == *"canary"* ]]; then
      export RAILS_ENV=test
      sudo systemctl start docker
      sudo docker start inverater-postgres
      ...
  fi
  bundle exec rake db:migrate
  bundle exec whenever --update-crontab
  sudo systemctl start puma_ruby_backend
  ```
  El log real de CodeDeploy del 2026-04-15 18:08:44 confirma que tomó la rama "canary" (`Canary deployment detected...`) y exportó `RAILS_ENV=test`, y que **la migración falló**:
  ```
  rake aborted!
  LoadError: cannot load such file -- stripe_mock
  ```
  Causa raíz: `stripe-ruby-mock` (`require: 'stripe_mock'`) está en el grupo `test` de Gemfile, pero `install_dependencies.sh` ejecuta `bundle config set --local without 'development test'` — al forzar `RAILS_ENV=test`, Rails intenta cargar gemas del grupo test que nunca se instalaron. **CONFIRMADO** (log `scripts.log` de CodeDeploy, deployment `d-PQK5M2GXI`)

**Consecuencia operativa:** en el despliegue más reciente, las migraciones de base de datos no se aplicaron exitosamente, pero el servidor de aplicación se inició de todas formas bajo un `RAILS_ENV` distinto (`canary`) al usado durante la migración (`test`). El esquema de base de datos puede estar desincronizado del código desplegado. Además, `database.yml` no tiene bloque `production`, por lo que cualquier ejecución con `-e production` fallará al no encontrar configuración. **CONFIRMADO/INFERIDO**

### Cron / whenever — acumulación de tareas duplicadas

- El crontab de `deploy` contiene **12 bloques** `# Begin/End Whenever generated tasks...` acumulados desde 2024-09-13 hasta 2026-04-15, cada uno con una ruta de origen distinta (`releases/<timestamp>/config/schedule.rb`), por lo que `whenever --update-crontab` nunca los reemplaza entre sí (identifica bloques por ruta exacta). **CONFIRMADO** — `crontab -u deploy -l`
- Efecto medido hoy en `/var/log/syslog`: a las 09:00:01 UTC del 2026-07-24 se dispararon **10 invocaciones distintas** de `SendPaymentRemindersJob` (mezclando `-e production`, `-e canary`, `script/runner` y `bin/rails runner`). **CONFIRMADO**
- Como `database.yml` no define entorno `production`, las invocaciones con `-e production` muy probablemente fallan en tiempo de ejecución (no se ejecutó ninguna prueba activa para confirmarlo, por regla de no ejecutar jobs). **INFERIDO**
- `config/schedule.rb` define 4 tareas diarias: `CampaignHelper.publish_projects!`, `SendPaymentRemindersJob`, `CancelExpiredSTPTransactionsJob`, `CheckOverduePaymentsJob` (una quinta, `BartecSyncUserBalance`, está comentada). CONFIRMADO

### Logs y almacenamiento

- `/home/deploy/shared/log/canary.log`: **1.49 GB**, sin `logrotate` configurado (no se encontró entrada en `/etc/logrotate.d/`). Riesgo de crecimiento no controlado en un disco al 82%. CONFIRMADO
- Se detectó un archivo con nombre anómalo en el mismo directorio: `puts Account.first.email if Account.first.log` (0 bytes) — el nombre sugiere que en algún momento se ejecutó un comando shell con código Ruby sin comillas que terminó creándose como nombre de archivo por redirección accidental. El archivo está vacío (no expone datos), pero es indicio de un incidente operativo pasado que vale la pena investigar con el equipo de desarrollo. CONFIRMADO (existencia) / INFERIDO (causa)
- Permisos de socket Puma: `/home/deploy/shared/tmp/sockets/puma.sock` = `srw-rw-rw-` (666); `/home/deploy/current/tmp/sockets/puma.sock` = `srwxrwxrwx` (777) — cualquier usuario local puede conectar directamente al socket de Rails sin pasar por NGINX. CONFIRMADO

### Permisos de archivos sensibles (mundialmente legibles)

| Archivo | Permisos | Riesgo |
|---|---|---|
| `/home/deploy/.rails_env` | 644 (rw-rw-r--) | RAILS_ENV visible por cualquier usuario local |
| `/home/deploy/.cron_env` | 664, contiene la variable `RAILS_MASTER_KEY` (valor no mostrado) | Cualquier usuario local del sistema puede leer la master key de Rails |
| `/home/deploy/shared/config/application.yml` | 644 | Legible por cualquier usuario del sistema |
| `/home/deploy/shared/config/database.yml` | 644 | Legible por cualquier usuario del sistema |
| `/home/deploy/shared/config/master.key` | 644 | Legible por cualquier usuario del sistema |
| `/home/deploy/shared/config/credentials/{canary,test}.key` | 644 | Legible por cualquier usuario del sistema |

CONFIRMADO — `ls -la`. Ningún valor de estos archivos fue mostrado en este informe.

### Variables de entorno (solo nombres, sin valores)

De `application.yml` (bloque `canary:`): `application_domain`, `application_port`, `slack_webhook_url`, `checkout_session_success_url`, `checkout_session_cancel_url`, `stripe_transaction_url`, `admin_stripe_transaction_url`, `shuftipro_success_url`, `default_currency`, `carrierwave_asset_host`, `cete_certificate_url`, `complete_profile_url`, `campaigns_url`, `campaign_url`, `property_complex_url`, `homepage_url`, `tech_support_email`, `register_referral_url`, múltiples `*_id` de plantillas Mailjet, `flow_id_wpp_code`, `flow_id_wpp`, `user_services_url`, `flow_id_adress`, `truora_api_key`, `stripe_webhook_secret`, `COOKIE_DOMAIN`, `ALLOWED_ORIGINS`, `go_api_url`, `mixpanel_token_dev`, `mixpanel_token_prod`. CONFIRMADO (nombres de clave, sin valores)

De `database.yml`: bloques `default` (adapter/encoding/host/pool), `canary` (host/database/username/password), `test` (database/username/password) — **no existe bloque `production`**. CONFIRMADO

### Versión / release

- Ruby 3.1.3, Rails 7.0.8.7. CONFIRMADO — `.ruby-version`, `Gemfile.lock`
- Último SHA registrado en `revisions.log`: `7491eea777fcae16ffb778be81dd06aa41bcdb0c` (rama `development`, release `20260313195913`, 2026-03-13). El despliegue del 2026-04-15 **no quedó registrado** en `revisions.log` porque usó el flujo CodeDeploy (que copia directo a `ruby-backend`, sin pasar por el mecanismo que escribe ese log). El SHA exacto del código actualmente corriendo no pudo confirmarse con certeza total porque `ruby-backend` no es un checkout git. INFERIDO
- Quedan remanentes de Capistrano en el código desplegado: `Capfile`, `config/deploy.rb`, `config/deploy/{canary,production,staging}.rb` — no se encontró evidencia de que Capistrano se use activamente hoy (el flujo activo es CodeDeploy). CONFIRMADO (presencia de archivos) / INFERIDO (inactividad)

---

## 6. Backend Go

- **Unidad systemd:** `go-api.service` — `active (running)` desde 2026-04-15 17:57:58 (PID 2298). Usuario/grupo `godeploy`. `WorkingDirectory=/srv/go-api/current`. `EnvironmentFile=/srv/go-api/shared/.env`. `ExecStart=/srv/go-api/current/app`. `Restart=always`, `RestartSec=3`. Logs → journald. CONFIRMADO — `systemctl cat go-api.service`

### Hallazgo crítico: binario en ejecución borrado del disco

- `/srv/go-api/current` → symlink → `/srv/go-api/release` (sin timestamp, **no** sigue el patrón usado por el propio script de despliegue `deploy_release.sh`, que crea `releases/<TS>` y enlaza `current` a esa ruta con timestamp).
- `/srv/go-api/release` **no existe actualmente** en el filesystem (`ls`: "No such file or directory"). CONFIRMADO
- El proceso en ejecución (PID 2298) mantiene el binario vivo solo por el inodo abierto: `/proc/2298/exe -> /srv/go-api/release/app (deleted)`. CONFIRMADO — `readlink /proc/2298/exe`
- **Consecuencia:** si el proceso se cae o el servicio se reinicia por cualquier motivo (crash, `Restart=always` tras OOM, reinicio de instancia, deploy fallido), `systemd` intentará ejecutar `/srv/go-api/current/app`, que apunta a una ruta inexistente, y el servicio **no podrá arrancar**. Es un riesgo de indisponibilidad total del backend Go pendiente de "explotar" en el próximo reinicio. CONFIRMADO
- Los scripts oficiales de CodeDeploy para Go (`set_permissions.sh`) sí hacen `ln -sfn /srv/go-api/release /srv/go-api/current` (sin timestamp) — es decir, **el propio hook de CodeDeploy usa la ruta `release` sin timestamp**, distinta del script manual `bin/deploy_release.sh` (que sí usa `releases/<TS>`). Son dos mecanismos de despliegue inconsistentes conviviendo en el mismo servidor; el que se usó en el último despliegue (CodeDeploy) deja el symlink apuntando a una carpeta que luego fue eliminada (posiblemente por un `rm -rf` manual o un script de limpieza no identificado). CONFIRMADO (evidencia de ambos scripts) / INFERIDO (causa exacta del borrado)
- `/srv/go-api/releases/` conserva 5 releases históricos del script manual, el más reciente del 2026-02-16 18:21 (13.8 MB) — anterior al despliegue actual, por lo que no representan el binario corriendo hoy. CONFIRMADO

### Configuración

- `/srv/go-api/shared/.env`: permisos **600** (`-rw-------`, propietario `godeploy`), correctamente restringido — a diferencia de los archivos de configuración de Rails. CONFIRMADO
- Nombres de variables declaradas en `.env` (sin valores): `CORS_ALLOWED_ORIGINS`, `DATABASE_URL`, `ENV`, `FRONTEND_URL`, `JWT_SECRET`, `MIXPANEL_TOKEN_DEV`, `MJ_APIKEY_PRIVATE`, `MJ_APIKEY_PUBLIC`, `MJ_TEMPLATE_CAMPAIGN_ACQUIRED_ID`, `MJ_TEMPLATE_OVERPAYMENT_ID`, `MJ_TEMPLATE_PARTIAL_PAYMENT_ID`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`. CONFIRMADO — el propio archivo `.env` no fue mostrado ni copiado.
- El archivo es cargado por **systemd** vía `EnvironmentFile`, no por el binario. CONFIRMADO
- Bind: `127.0.0.1:8000` únicamente (no expuesto directamente a la red, solo accesible vía NGINX). CONFIRMADO — `ss -tulpn`, `lsof -p 2298`
- Conectividad a base de datos: no se observó una conexión TCP activa al puerto 5432 en el momento de la revisión (probablemente por pool de conexiones inactivo en ese instante); dado que `DATABASE_URL` no puede exponerse (regla de seguridad), la confirmación directa del host/puerto usado por Go queda **NO VERIFICABLE DESDE EC2** sin violar la política de no imprimir `.env`. Es **INFERIDO** que Go se conecta al mismo Postgres Docker local, dado que es la única instancia de Postgres presente en el host.
- Metadatos de binario: el binario actualmente en ejecución no puede inspeccionarse (archivo borrado); el release histórico más reciente disponible en disco (`20260216182033/app`) pesa 13,807,800 bytes, del 2026-02-16 18:21. No representa necesariamente el binario corriendo hoy. INFERIDO

### Logs y salud

- Logs vía `journalctl -u go-api`. Se observan errores recurrentes y actuales: `[bartec.RequestCampaignBalances] Failed to parse response: invalid character '<' looking for beginning of value` — indica que el endpoint de BARTeC/STP está devolviendo HTML (probablemente una página de error) en lugar de JSON, de forma repetida y reciente (últimas horas). CONFIRMADO
- `NRestarts=0` — el servicio no se ha reiniciado desde que arrancó el 2026-04-15 (coherente con el hecho de que el binario borrado nunca fue puesto a prueba por un restart). CONFIRMADO

---

## 7. PostgreSQL/PostGIS en Docker

- **Servicio Docker:** `active (running)` desde 2026-04-15 17:50:02. CONFIRMADO — `systemctl status docker`
- **Contenedores existentes:**

| Nombre | Imagen | Estado | Puertos | Creado |
|---|---|---|---|---|
| `inverater-postgres` | `postgis/postgis:14-3.3` | Up (3 meses) | `0.0.0.0:5432->5432/tcp`, `:::5432->5432/tcp` | 2023-02-23 |
| `inverater-redis` | `redis` | Up (3 meses) | `0.0.0.0:6379->6379/tcp`, `:::6379->6379/tcp` | 2023-03-06 |

CONFIRMADO — `docker ps -a`

### `inverater-postgres` en detalle

- **RestartPolicy:** `no` (el contenedor no se reinicia automáticamente si Docker o el host se reinician, salvo por el hook de despliegue de Rails que hace `docker start inverater-postgres`). CONFIRMADO
- **Health check:** ninguno configurado (`Health: none`). CONFIRMADO
- **Red:** `bridge` (red Docker por defecto, sin red personalizada nombrada), IP interna `172.17.0.3`. CONFIRMADO
- **Volumen:** tipo `volume` (nombrado, gestionado por Docker) montado en `/var/lib/postgresql/data` dentro del contenedor, con datos en el host en `/var/lib/docker/volumes/818f5e159e.../​_data` (221 MB en uso). El volumen **persiste** independientemente de que el contenedor se recree (es un volumen con nombre, no un contenedor efímero), siempre que no se ejecute `docker volume rm`. CONFIRMADO
- **Variables de entorno del contenedor (solo nombres):** `POSTGRES_PASSWORD`, `POSTGRES_USER`, `PATH`, `GOSU_VERSION`, `LANG`, `PG_MAJOR`, `PG_VERSION`, `PGDATA`, `POSTGIS_MAJOR`, `POSTGIS_VERSION`. No se detectó `POSTGRES_DB` explícito. Ningún valor fue mostrado. CONFIRMADO
- **Versión Postgres/PostGIS:** por etiqueta de imagen, `postgis/postgis:14-3.3` implica PostgreSQL 14.x + PostGIS 3.3.x. INFERIDO (no se pudo ejecutar `SELECT version()` sin usar credenciales de aplicación no disponibles de forma segura; se intentó conexión de solo lectura sin credenciales vía `psql -U postgres`, que falló porque ese rol no existe en la instancia — comportamiento normal de imágenes Docker de Postgres inicializadas con un usuario personalizado).
- **`pg_isready`:** `accepting connections` en `/var/run/postgresql:5432`. CONFIRMADO — `docker exec inverater-postgres pg_isready` (sin exponer credenciales)
- **Exposición del puerto:** publicado explícitamente en `0.0.0.0:5432` (todas las interfaces), reenviado por `docker-proxy` e `iptables` (chain `DOCKER` acepta tráfico hacia `172.17.0.3:5432`). No hay firewall local (`ufw` inactivo) que lo bloquee. El alcance real desde Internet depende del Security Group "Rails Web Server", que **no se pudo verificar** desde esta instancia (sin AWS CLI, sin credenciales de rol solicitadas). CONFIRMADO (local) / NO VERIFICABLE DESDE EC2 (alcance por Internet)
- **Conexión Rails↔Postgres:** `database.yml` (bloque `canary`) usa `host: 0.0.0.0`, es decir, Rails se conecta al puerto publicado en el propio host (no vía red interna de Docker), consistente con el `docker-proxy` observado. CONFIRMADO
- **Conexión Go↔Postgres:** no verificable sin exponer `DATABASE_URL`; INFERIDO que apunta al mismo Postgres local, por ser la única instancia disponible.
- **¿Rails y Go comparten el mismo contenedor/base de datos?** INFERIDO que sí (mismo host, mismo puerto publicado, sin otra instancia de Postgres presente), pero no confirmado con 100% de certeza sin inspeccionar el valor de `DATABASE_URL` de Go, lo cual está prohibido por las reglas de esta auditoría.

### Backups

- Único backup encontrado: `/home/deploy/backups/inverater-canary20241107.dump` (3.5 MB, fechado 2024-11-07 — **más de 20 meses de antigüedad** respecto a la fecha de esta auditoría). CONFIRMADO
- `pg_dump@.timer` (unidad systemd de plantilla incluida por `postgresql-common`) existe pero está **`disabled`** y no instanciada para ninguna base de datos concreta. No hay backups automatizados activos. CONFIRMADO
- No se encontró script, cron ni documentación de un procedimiento de **restauración probado**. CONFIRMADO (ausencia de evidencia)

---

## 8. WordPress y servicios auxiliares

- No se encontró proceso, paquete, unidad systemd, contenedor Docker ni configuración de NGINX relacionados con WordPress, PHP o Apache en esta instancia. CONFIRMADO — búsqueda de procesos, `which php/php-fpm/apache2`, `find -iname wp-config.php`, grep de configuración NGINX.
- **Conclusión:** WordPress no corre en Canary; probablemente se sirve desde un origen externo (S3/CloudFront, dado que `test.inverater.com` resuelve a CloudFront) o desde otra instancia no auditada aquí. INFERIDO (ubicación exacta fuera del alcance de esta instancia)
- No se encontró Redash ni ningún otro servicio auxiliar corriendo localmente (ni como proceso, ni como contenedor Docker). CONFIRMADO

---

## 9. S3 y servicios externos

### S3

- El rol IAM adjunto a la instancia se llama **`EC2ReadBuckets`** (nombre reportado únicamente; no se solicitaron ni expusieron credenciales temporales). CONFIRMADO — IMDSv2
- No se encontraron credenciales estáticas de AWS (`~/.aws/`) para ningún usuario del sistema (`deploy`, `godeploy`, `ubuntu`, `root`) — el acceso a AWS se realiza vía **rol de instancia**, no claves estáticas. CONFIRMADO
- Rails usa el gem `fog-aws` (a través de CarrierWave) para subir archivos a S3 en los entornos `canary` y `production`; el bucket (`fog_directory`) y la región se leen de `Rails.application.credentials.dig(:aws_s3, ...)`, es decir, **están cifrados en las credenciales de Rails y no se pudieron leer sin exponer secretos** (por diseño de esta auditoría). CONFIRMADO (mecanismo) / NO VERIFICABLE DESDE EC2 (nombre exacto del bucket sin descifrar credentials)
- `test.inverater.com` (frontend de Canary) resuelve por DNS a CloudFront (`d2zr10hfvxvrke.cloudfront.net`), consistente con un origen S3 + CloudFront típico de un frontend estático — la instancia EC2 auditada **no aloja** este frontend. CONFIRMADO (resolución DNS) / INFERIDO (arquitectura S3+CloudFront detrás de CloudFront)
- No se encontró el bucket `inverater-canary-uploads` (ni ningún literal de nombre de bucket) en el código fuente ni en la configuración inspeccionada sin descifrar credenciales — su nombre exacto está en `Rails.application.credentials` (cifrado). NO VERIFICABLE DESDE EC2
- No se realizó ninguna operación de lectura/escritura/listado sobre buckets S3 reales durante esta auditoría.

### Integraciones externas (nombre → host, solo de código/logs, sin llamadas salientes realizadas)

| Integración | Host(s) observado(s) | Evidencia |
|---|---|---|
| Stripe | `api.stripe.com`, `connect.stripe.com`, `files.stripe.com`, `meter-events.stripe.com` | CONFIRMADO — conexión TCP activa real desde Puma (`CLOSE_WAIT` a `api-34-200-27-109.stripe.com:443`) + strings del binario Go + `STRIPE_SECRET_KEY`/`STRIPE_WEBHOOK_SECRET` (solo nombres) |
| Mailjet | `api.mailjet.com`, `in-v3.mailjet.com` | CONFIRMADO — gem `mailjet` en Gemfile, strings del binario Go, variables `MJ_APIKEY_*` (solo nombres) |
| BARTeC / STP | `stp.inverater.com` (mismo host para ambos; "BARTeC" es el nombre de la librería Ruby/Go que integra con el sistema STP) | CONFIRMADO — `lib/bartec/client.rb: base_uri Figaro.env.bartec_base_url \|\| 'https://stp.inverater.com'`; errores activos en logs de Go (`bartec.RequestCampaignBalances`) |
| Intercom | `api.intercom.io` | CONFIRMADO — `app/helpers/intercom_helper.rb` |
| Truora | `api.account.truora.com`, `api.connect.truora.com` (WhatsApp), `api.identity.truora.com`, `api.validations.truora.com` | CONFIRMADO — `app/services/truora_*.rb`, `zapsign_validation_service.rb`; variable `truora_api_key` (solo nombre) |
| DocuSign | No se encontró host activo en el código actual; existe un archivo `docusign.log` vacío y una rama de git histórica `docusign_delete`, lo que sugiere que la integración fue removida o está inactiva | INFERIDO |
| Shuftipro | rutas `shuftipro#webhook`, `shuftipro#url` presentes en `config/routes/`, pero existe una rama `remove_shufti` — posible integración en desuso, reemplazada por Truora | INFERIDO |
| Mixpanel | tokens dev/prod (solo nombres de variable) | CONFIRMADO (presencia) |
| WordPress | ninguna llamada saliente encontrada desde Rails/Go hacia un host de WordPress específico en esta revisión | NO VERIFICABLE DESDE EC2 |

No se realizó ninguna llamada a estos servicios externos durante la auditoría; no se expusieron API keys, tokens ni payloads.

---

## 10. Flujo de despliegue

### CodeDeploy (mecanismo activo confirmado)

- **Agente:** `codedeploy-agent.service`, activo desde 2026-04-15 17:50:00, versión **`OFFICIAL_1.8.1-26_deb`**, haciendo *long-polling* continuo (`poll_host_command`) cada ~45s en el momento de la revisión. CONFIRMADO
- **`appspec.yml` (Rails)**, leído como texto (nunca ejecutado por esta auditoría):
  ```yaml
  files:
    - source: /
      destination: /home/deploy/ruby-backend
  hooks:
    BeforeInstall:    scripts/codedeploy/stop_server.sh      (runas: deploy)
    AfterInstall:     scripts/codedeploy/set_permissions.sh  (runas: root)
                       scripts/codedeploy/install_dependencies.sh (runas: deploy)
    ApplicationStart: scripts/codedeploy/start_server.sh     (runas: deploy)
  ```
  CONFIRMADO

### Secuencia reconstruida del hook de Rails (`start_server.sh`)

1. `systemctl stop puma_ruby_backend` (BeforeInstall)
2. `chown -R deploy:deploy` + `chmod 755` sobre `ruby-backend` (AfterInstall)
3. `ln -sfn ruby-backend current`, `bundle install --without development test` (AfterInstall)
4. (ApplicationStart) crea directorios `tmp/*`, enlaza `database.yml`/`application.yml`/`master.key`/`credentials/*.key` compartidos
5. **Si `DEPLOYMENT_GROUP_NAME` contiene "canary"**: enlaza `credentials/canary.key`, exporta `RAILS_ENV=test`, hace `systemctl start docker`, `docker start inverater-postgres`, `sleep 5`
6. Si no, exporta `RAILS_ENV=production` (rama de "producción", que en esta instancia nunca se ejecuta porque siempre es grupo canary)
7. `bundle exec rake db:migrate` — **falló en el último despliegue real** (`LoadError: stripe_mock`, ver sección 5)
8. `bundle exec whenever --update-crontab` — genera cron con `-e production` por defecto (el gem `whenever` no hereda el `RAILS_ENV` exportado en el paso 5/6 salvo que se le pase `-e` explícitamente, lo cual el script no hace)
9. `systemctl start puma_ruby_backend` — arranca con `RAILS_ENV=canary` real, vía `.rails_env`, independiente de todo lo anterior

**Respuestas explícitas a lo solicitado:**
- ¿Inicia Docker? **Sí, CONFIRMADO** (paso 5)
- ¿Inicia `inverater-postgres`? **Sí, CONFIRMADO** (`docker start inverater-postgres`, paso 5)
- ¿Fija `RAILS_ENV=test`? **Sí, CONFIRMADO** — pero solo dentro del proceso del script de despliegue (migración/whenever); el proceso Puma real corre con `RAILS_ENV=canary` por un mecanismo independiente (`.rails_env` vía systemd)
- ¿Ejecuta migraciones? **Sí lo intenta, pero falló en el despliegue más reciente registrado.** CONFIRMADO
- ¿Actualiza el crontab de whenever? **Sí, CONFIRMADO**, pero de forma acumulativa/defectuosa (ver sección 5)
- ¿Inicia Puma? **Sí, CONFIRMADO**

### `appspec.yml` / hooks de Go

```yaml
BeforeInstall:    stop_server.sh → systemctl stop go-api || true
AfterInstall:     set_permissions.sh → chmod +x /srv/go-api/release/app; chown godeploy; ln -sfn release current
ApplicationStart: start_server.sh → systemctl start go-api
```
CONFIRMADO — logs de `scripts.log` del despliegue `d-FMJ5RHEXI`/`d-87UTB74UI`, ejecutados 2026-04-15 17:45–17:57.

### Capistrano

- Quedan **remanentes de configuración** de Capistrano en el código Rails (`Capfile`, `config/deploy.rb`, `config/deploy/{canary,production,staging}.rb`) y un historial de despliegues estilo Capistrano en `/home/deploy/revisions.log` y `/home/deploy/releases/` (hasta 2026-03-13). No hay evidencia de que Capistrano se ejecute activamente hoy — el mecanismo vigente desde 2026-04-15 es CodeDeploy, que despliega directo a `ruby-backend` (no a `releases/<TS>`). CONFIRMADO (remanentes) / INFERIDO (Capistrano inactivo)

### Bitbucket vs GitHub Actions

- El repositorio remoto es **Bitbucket** (`git@bitbucket.org:inverater-frontend/inverater-backend.git`), con `bitbucket-pipelines.yml` presente definiendo pipelines para las ramas **`development`** y **`master`**. No se encontró configuración de GitHub Actions (`.github/workflows`) en el código desplegado. CONFIRMADO
- Conclusión: el pipeline CI/CD usa **Bitbucket Pipelines → AWS CodeDeploy**, no GitHub Actions. CONFIRMADO

### Go — mecanismo de despliegue manual paralelo

- Existen scripts manuales en `/srv/go-api/bin/`: `deploy_release.sh` (extrae tarball a `releases/<TS>`, actualiza symlink `current → releases/<TS>`, reinicia el servicio, purga releases antiguos) y `rollback.sh`. Estos **no coinciden** con el mecanismo usado por los hooks de CodeDeploy (que usan `release` sin timestamp). La coexistencia de ambos mecanismos es la causa más probable del symlink roto descrito en la sección 6. CONFIRMADO (existencia de ambos scripts) / INFERIDO (causa del binario borrado)

---

## 11. Autenticación y CORS

### CORS — Rails

```ruby
# config/initializers/cors.rb
allow do
  origins '*'
  resource '*', headers: :any, methods: [...],
           expose: %w[Authorization Role AccountID Name Email HMACSecret OTPVerified
                       Content-Disposition MasterbrokerID MasterbrokerName],
           credentials: false
end
```
CONFIRMADO. El comentario del archivo dice "In development, allow any origin", pero **el wildcard `origins '*'` se aplica sin condicionar por `Rails.env`** — es decir, se aplica igual en canary y (salvo que exista otro initializer no encontrado) en producción. `credentials: false` mitiga parcialmente el riesgo (los navegadores no permiten `credentials: include` combinado con origen wildcard), pero la exposición de headers sensibles (`Authorization`, `HMACSecret`) a cualquier origen sigue siendo una configuración excesivamente permisiva. **Hallazgo de seguridad — ver sección 14.**

### CORS — Go

- Variable de entorno `CORS_ALLOWED_ORIGINS` declarada en `.env` (nombre confirmado, valor no expuesto por política de esta auditoría). NO VERIFICABLE DESDE EC2 (valor)

### Flujo de autenticación

- **Rails:** no se encontró `session_store.rb` ni uso de cookies `user`/`user_token` en los controladores. La autenticación observada usa **JWT vía header `Authorization: Bearer`** (`response.headers['Authorization'] = "Bearer #{token}"` en `sponsors/auth_controller.rb`), y existe un middleware `lib/middleware/jwt_cookie_middleware.rb` que usa `ENV['COOKIE_DOMAIN']` (por defecto `.inverater.com` si no está seteada) — sugiere que en ciertos flujos el JWT también se propaga como **cookie** con dominio configurable. CONFIRMADO (código) — no se inspeccionaron cookies reales de sesión.
- **Go:** variable `JWT_SECRET` declarada (nombre solamente). Cadenas encontradas en el binario sugieren uso de cookies con atributo `SameSite=Strict`, pero esta evidencia proviene de un volcado de `strings` sobre un binario Go compilado (no del código fuente), por lo que se reporta como **INFERIDO**, no confirmado con certeza estructural.
- **Mismatch potencial Canary-only:** `COOKIE_DOMAIN` tiene un valor por defecto de `.inverater.com` en el middleware Rails; si en Canary la variable de entorno `COOKIE_DOMAIN` no está seteada explícitamente a un dominio de canary (p. ej. algo bajo `test.inverater.com`/`api-test.inverater.com`), las cookies de sesión emitidas por Rails podrían fijarse para el dominio de producción y no ser utilizables por el frontend de canary, o viceversa. **No se pudo confirmar el valor real de `COOKIE_DOMAIN` en canary** sin exponer el contenido de `application.yml`. NO VERIFICABLE DESDE EC2 (valor exacto) — se documenta como posible causa de fallos de autenticación específicos de Canary.

---

## 12. Logs, monitoreo y health checks

### Estado systemd (resumen)

| Servicio | Estado | Desde |
|---|---|---|
| nginx | active (running) | 2026-07-24 06:33 |
| puma_ruby_backend | active (running) | 2026-04-15 18:08 |
| puma.service (legado) | **failed** | 2026-04-15 17:50 |
| go-api | active (running) | 2026-04-15 17:57 |
| docker | active (running) | 2026-04-15 17:50 |
| codedeploy-agent | active (running) | 2026-04-15 17:50 |
| WordPress / Redash | no instalado / no presente | — |

CONFIRMADO — `systemctl status <unit>`. Ningún servicio fue reiniciado, iniciado ni detenido durante esta auditoría.

### Reinicios recientes

- `NRestarts=0` para `puma_ruby_backend`, `go-api`, `docker`, `nginx`, `codedeploy-agent` — sin reinicios desde el último arranque (2026-04-15). CONFIRMADO. (`puma.service` legado no cuenta reinicios porque está en estado failed permanente, no en loop de reinicio activo.)

### CloudWatch Agent

- No se encontró el paquete/servicio `amazon-cloudwatch-agent` instalado en esta instancia. CONFIRMADO (ausencia)

### Riesgos de disco y logs

- `canary.log`: 1.49 GB, sin logrotate. CONFIRMADO
- Múltiples copias duplicadas de la gema `wkhtmltopdf-binary` (versiones 0.12.6.6/.8/.9) cacheadas en `~/.rbenv`, `shared/bundle` y `ruby-backend/vendor/bundle`, sumando >1.5 GB adicionales de espacio en disco. CONFIRMADO
- Disco raíz al 82% de uso (5.5 GB libres de 29 GB). CONFIRMADO — riesgo real de llenado de disco que tumbaría todos los servicios (Postgres, logs, la propia app) simultáneamente.

### Health endpoints

- No se generó carga significativa ni se realizaron peticiones HTTP activas contra la aplicación durante esta auditoría. Se identificaron rutas de tipo *health/ping* en cadenas del binario Go (`/admin/ping`, `/account/me`) por inspección estática, sin invocarlas. INFERIDO

### Muestra de logs (redactada)

- Se revisó una muestra pequeña de `journalctl -u go-api` (últimas ~10 líneas) y de `/var/log/syslog` (entradas `CRON`), ambas ya citadas en las secciones 5 y 6. No se volcó ningún log completo; no se muestran tokens, cookies, cabeceras de autorización ni datos personales.

---

## 13. Diferencias frente a Producción

Esta auditoría **no asume que Canary sea representativo de Producción**. Diferencias observadas o inferidas relevantes para la validez de pruebas en Canary:

1. **RAILS_ENV real**: Canary corre con `RAILS_ENV=canary` (bloque propio en `database.yml` y `application.yml`), un tercer entorno distinto de `test` y `production`, con su propia base de datos (`canary`) — probablemente distinta de la base de datos de RDS que usaría producción (`start_server.sh` documenta explícitamente: en producción "Relying on AWS RDS", en canary "Ensuring local Docker DB"). Esto es una diferencia estructural fundamental: **Canary usa Postgres local en Docker; Producción usaría RDS gestionado por AWS** (según el propio script, no verificado directamente porque este host no es producción). CONFIRMADO (script) / NO VERIFICABLE DESDE EC2 (infraestructura real de producción)
2. **Capacidad de hardware**: t2.micro (1 vCPU, ~1GB RAM) — muy probablemente menor a la instancia de producción. INFERIDO
3. **Sin health checks en el contenedor de Postgres**, sin `RestartPolicy`, sin backups automatizados — es dudoso que producción opere con la misma falta de resiliencia, pero no se pudo verificar. NO VERIFICABLE DESDE EC2
4. **CORS wildcard (`origins '*'`)** aplicado sin condicionar por entorno — si el mismo `cors.rb` se despliega a producción, el mismo riesgo aplicaría allí. INFERIDO (mismo código fuente compartido entre entornos vía la misma rama de Rails)
5. **Migraciones fallidas por `stripe_mock`** son un problema específico del flujo de despliegue de canary (rama `DEPLOYMENT_GROUP_NAME contains "canary"` fuerza `RAILS_ENV=test`); el bloque `else` (producción) no tiene este problema porque exporta `RAILS_ENV=production` en lugar de `test`. CONFIRMADO (lógica del script)
6. **Bugs del cron `whenever`** (acumulación de bloques) probablemente también afectan a producción si comparte el mismo patrón de despliegue con rutas de release cambiantes — no verificado en esta instancia por no ser el host de producción. NO VERIFICABLE DESDE EC2

**Conclusión:** por las diferencias de entorno de base de datos (Docker local vs. RDS) y de capacidad de hardware, los resultados de pruebas funcionales en Canary **no deben considerarse equivalentes** a un comportamiento garantizado en Producción, incluso si el mismo código se despliega en ambos.

---

## 14. Riesgos y hallazgos

Ordenados de mayor a menor severidad operativa/seguridad:

1. **[ALTA] Binario Go en ejecución borrado del disco.** Un reinicio del servicio o un crash dejará el backend Go completamente caído hasta un redeploy manual. Sección 6. CONFIRMADO
2. **[ALTA] Migraciones de base de datos fallando en cada despliegue de Canary** (`stripe_mock` LoadError bajo `RAILS_ENV=test`), combinado con una discrepancia entre el entorno de migración (`test`) y el de ejecución real (`canary`). Riesgo de esquema desincronizado. Sección 5/10. CONFIRMADO
3. **[ALTA] Cron `whenever` acumulando bloques duplicados**, ejecutando tareas de negocio (recordatorios de pago, cancelación de transacciones STP) hasta 10 veces por ejecución programada, algunas contra un entorno Rails (`production`) sin configuración de base de datos válida. Sección 5. CONFIRMADO
4. **[ALTA] `deploy` tiene sudo `(ALL:ALL) ALL`** además de `NOPASSWD` sin restricciones sobre `systemctl` y `docker` — privilegio equivalente a root completo desde una cuenta de aplicación. Sección 12 (sudoers). CONFIRMADO
5. **[ALTA] PostgreSQL y Redis publicados en `0.0.0.0`** sin firewall local (`ufw` inactivo); el alcance real depende exclusivamente del Security Group de AWS, no verificado. Sección 7. CONFIRMADO (local) / NO VERIFICABLE DESDE EC2 (alcance)
6. **[MEDIA-ALTA] Archivos de configuración con secretos world-readable** (`application.yml`, `database.yml`, `master.key`, `.rails_env`, `.cron_env` con `RAILS_MASTER_KEY`) — permisos 644/664 en vez de 600/640. Contrasta con el `.env` de Go, correctamente restringido a 600. Sección 5. CONFIRMADO
7. **[MEDIA] Sin backups automatizados de Postgres**; único dump disponible tiene >20 meses de antigüedad; sin procedimiento de restauración documentado o probado. Sección 7. CONFIRMADO
8. **[MEDIA] CORS con `origins '*'` sin restricción por entorno**, exponiendo headers sensibles (`Authorization`, `HMACSecret`) a cualquier origen. Sección 11. CONFIRMADO
9. **[MEDIA] Socket de Puma con permisos 666/777**, permitiendo que cualquier usuario local del sistema hable directo con Rails sin pasar por NGINX. Sección 5. CONFIRMADO
10. **[MEDIA] Disco al 82% con un log de 1.49 GB sin rotación** y múltiples gemas binarias duplicadas cacheadas — riesgo de llenado de disco que derribaría todos los servicios simultáneamente. Sección 12. CONFIRMADO
11. **[BAJA-MEDIA] Dos unidades systemd de Puma conviviendo** (`puma.service` failed + `puma_ruby_backend.service` activo), ambas dependientes del mismo `puma.socket` — configuración residual confusa que debería limpiarse. Sección 5. CONFIRMADO
12. **[BAJA] Dos flujos de despliegue distintos y mutuamente inconsistentes para Go** (script manual con releases con timestamp vs. hook CodeDeploy sin timestamp) — causa más probable del hallazgo #1. Sección 10. INFERIDO
13. **[INFORMATIVA] `RAILS_ENV=test` mencionado en el prompt de auditoría no es el valor real de ejecución** — el proceso Puma corre bajo `RAILS_ENV=canary`; `test` solo se usa transitoriamente y de forma fallida durante el hook de migración. Esto es una aclaración importante frente a la hipótesis de partida de esta auditoría. CONFIRMADO
14. **[INFORMATIVA] Archivo de nombre anómalo** `puts Account.first.email if Account.first.log` (vacío) en el directorio de logs compartidos — indicio de un incidente operativo pasado (comando mal escapado), sin datos expuestos hoy. Sección 5. CONFIRMADO (existencia, vacío) / INFERIDO (causa)

No se identificaron: procesos corriendo como root de forma innecesaria (Puma corre como `deploy`, Go como `godeploy`, no como root); credenciales AWS estáticas (se usa rol de instancia); secretos visibles en línea de comandos de procesos (`ps -eo cmd` no mostró patrones de password/secret/token/key); ausencia de redirección HTTP→HTTPS (sí existe, 301 configurado en ambos vhosts).

---

## 15. Datos pendientes de validar en AWS

Los siguientes puntos requieren acceso a la consola o API de AWS (no disponible desde esta instancia sin instalar/autenticar AWS CLI y sin usar las credenciales temporales del rol IAM, que esta auditoría evitó solicitar deliberadamente):

- Reglas exactas del Security Group **"Rails Web Server"** — en particular si los puertos 5432 (Postgres) y 6379 (Redis) están abiertos a `0.0.0.0/0` o restringidos a rangos internos/VPN.
- Política IAM completa asociada al rol **`EC2ReadBuckets`** (qué buckets S3 puede leer/escribir realmente, pese a lo que sugiere su nombre).
- Nombre exacto y región de los buckets S3 usados por CarrierWave (`fog_directory`, cifrado en `Rails.application.credentials`).
- Configuración real de CloudFront/S3 detrás de `test.inverater.com`.
- Si existe una instancia RDS de producción y cómo se compara su configuración (versión, tamaño, backups automatizados) con el Postgres Docker de Canary.
- Historial completo de despliegues en la consola de CodeDeploy (esta auditoría solo pudo ver el paquete de despliegue más reciente conservado localmente).
- Confirmación de si existe una instancia EC2 separada para WordPress y su configuración de red.
- Alertas o dashboards de CloudWatch, dado que no se encontró el agente de CloudWatch instalado localmente.

---

## 16. Evidencia técnica

Comandos de solo lectura usados como base de esta auditoría (lista no exhaustiva, referenciada inline en cada sección):

```
uname -a ; cat /etc/os-release ; uptime ; free -h ; df -hT ; lsblk
curl IMDSv2 (token PUT + GET meta-data/*)  — sin solicitar iam/security-credentials/<role> completo
sudo ss -tulpn ; sudo ss -tnp state all
nginx -v ; systemctl status nginx ; cat /etc/nginx/sites-available/*.conf
openssl x509 -noout -issuer -subject -dates -ext subjectAltName
sudo ufw status verbose ; sudo iptables -L -n
systemctl status/cat puma_ruby_backend.service, puma.service, puma.socket, go-api.service, docker.service, codedeploy-agent.service
ls -la / stat sobre directorios y symlinks de /home/deploy y /srv/go-api
sudo cat /home/deploy/.rails_env  (solo variable RAILS_ENV, no secretos)
sudo crontab -u deploy -l ; cat config/schedule.rb ; grep CRON /var/log/syslog
sudo docker ps -a ; sudo docker inspect inverater-postgres/inverater-redis
sudo docker exec inverater-postgres pg_isready
sudo find / -iname "*pg_dump*" ; systemctl list-timers
grep de hostnames de integraciones en código fuente (app/, lib/, config/)
cat appspec.yml ; scripts/codedeploy/*.sh (leídos como texto, nunca ejecutados)
sudo cat CodeDeploy deployment-root logs (scripts.log, codedeploy-agent-deployments.log)
sudo -l -U deploy ; sudo -l -U godeploy ; getent group docker
```

Ningún comando de este listado modificó estado. No se ejecutaron migraciones, jobs de Rails, consultas SQL de escritura, reinicios de servicios/contenedores, ni llamadas a APIs externas.

---

## Revisión final de seguridad

Se releyó este informe completo antes de guardarlo. Se confirma que:

- No contiene contraseñas, tokens, claves privadas, API keys, cookies reales, ni URLs de conexión completas (host+usuario+contraseña) de ninguna base de datos o servicio.
- No contiene el contenido de `.env`, `master.key`, `application.yml`, `database.yml`, credenciales de Rails, ni archivos `EnvironmentFile` de systemd.
- No contiene información personal identificable de usuarios de la plataforma.
- Los únicos valores de configuración mostrados son: nombres de variables de entorno, nombres de dominios/hosts públicos, rutas de archivos, permisos Unix, nombres de servicios/contenedores/imágenes, y el valor no sensible `RAILS_ENV=canary` (explícitamente solicitado de determinar por el alcance de esta auditoría, y no una credencial).

**Se confirma explícitamente que ningún servicio, contenedor, archivo, base de datos ni recurso de AWS fue modificado, reiniciado, detenido, iniciado, recreado ni alterado como parte de esta auditoría.**