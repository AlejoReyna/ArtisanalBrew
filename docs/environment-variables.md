# Configuración de Variables de Entorno para Producción

Este documento describe cómo configurar las variables de entorno para ejecutar la aplicación en producción (EC2) usando una base de datos diferente a la de desarrollo.

## Esquema de Configuración

La aplicación usa un sistema de prioridad para las cadenas de conexión:

1. **Priority 1**: `ConnectionStrings__DefaultConnection` en `appsettings.json` o variables de entorno
2. **Priority 2**: Variables de entorno `DB_HOST`, `DB_NAME`, `DB_USERNAME`, `DB_PASSWORD`, `DB_PORT` (opcional, default: 5432)

## Variables de Entorno Requeridas para Producción

Crea un archivo `/etc/thiscafeteria/thiscafeteria.env` en tu EC2 con las siguientes variables:

```bash
# Base de Datos (producción)
DB_HOST=tu-host-produccion.com
DB_NAME=nombre_basedatos_produccion
DB_USERNAME=usuario_produccion
DB_PASSWORD=contraseña_segura_produccion
DB_PORT=5432  # opcional, default es 5432

# Opcional: Enviar emails desde producción
AWS_REGION=us-east-1
AWS_SENDSENDER_EMAIL=noreply@tudominio.com

# Opcional: Blockchain (si necesitas cambiar la red)
# Blockchain__Network__NetworkName=Ethereum Mainnet
# Blockchain__Network__RpcUrl=https://mainnet.infura.io/v3/...
```

## Uso con GitHub Actions

El archivo de entorno se copia a la EC2 durante el deploy. Para usar datos de producción:

### Opción A: Configurar directamente en la EC2

1. Conéctate a tu EC2:
   ```bash
   ssh -i tu-key.pem ubuntu@tu-ec2-public-dns
   ```

2. Crea el archivo de entorno:
   ```bash
   sudo mkdir -p /etc/thiscafeteria
   sudo nano /etc/thiscafeteria/thiscafeteria.env
   ```

3. Agrega las variables de entorno (ver arriba)

4. Reinicia el servicio:
   ```bash
   sudo systemctl restart thiscafeteria
   ```

### Opción B: Actualizar el workflow de GitHub Actions

Modifica el workflow para copiar un archivo `.env.production` durante el deploy:

1. Crea `.env.production` en tu repositorio (NO lo commitees, usa git secrets o repository secrets)
2. Agrega el contenido al secreto `EC2_ENV_FILE_CONTENT` en GitHub
3. Modifica el paso de deploy en `ci.yml`:

```yaml
- name: Deploy with environment file
  env:
    EC2_USER: ${{ secrets.EC2_USER }}
    ENV_FILE_CONTENT: ${{ secrets.EC2_ENV_FILE_CONTENT }}
  run: |
    set -euo pipefail
    # ... (pasos anteriores de copy artifact)
    
    # Create environment file on EC2
    ssh -i ~/.ssh/thiscafeteria-ec2 \
      -o BatchMode=yes \
      -o StrictHostKeyChecking=no \
      -o UserKnownHostsFile=/dev/null \
      -o ConnectTimeout=30 \
      -p "$EC2_SSH_PORT" \
      "${EC2_USER}@${EC2_HOST}" \
      "sudo mkdir -p /etc/thiscafeteria && \
       echo '$ENV_FILE_CONTENT' | sudo tee /etc/thiscafeteria/thiscafeteria.env > /dev/null && \
       sudo chmod 600 /etc/thiscafeteria/thiscafeteria.env"
    
    # ... (resto del deploy)
```

## Verificación

Después de configurar, verifica que:

1. La aplicación arranque sin errores:
   ```bash
   sudo journalctl -u thiscafeteria -f
   ```

2. La conexión a la base de datos funcione ( busca logs de successful connection)

3. El endpoint `/health` responda correctamente:
   ```bash
   curl http://tu-ec2-public-dns/health
   ```

## Seguridad

- **NUNCA** commitees archivos con credenciales reales (`.env`, `.env.production`)
- Usa `.gitignore` para excluir archivos sensibles
- Considera usar AWS Secrets Manager para producción en lugar de archivos de texto
- Limita permisos del archivo de entorno (`chmod 600`)