# Backend PHP de Gafetes Casco

Estado: preparado para despliegue, no desplegado.

Este paquete agrega el endpoint `POST /casco-api/api/gafetes/sync` sin reemplazar las rutas actuales de taxis. Se entrega como propuesta autonoma para integrar en el backend PHP real de Hostinger cuando exista acceso a `public_html/casco-api`.

## Modos incluidos

1. Integracion recomendada en router existente
   - Reutiliza `index.php`, `src/GafeteController.php` y las clases de `src/`.
   - Ruta esperada: `POST /api/gafetes/sync`
   - Recomendado porque no rompe el arbol actual de rutas y permite fusionar con el backend vivo.

2. Endpoint de emergencia independiente
   - Archivo: `api/gafetes/sync.php`
   - Puede copiarse de forma aislada si el router actual no es modificable de inmediato.

## Resumen funcional

- Acepta `branchCode = CV`
- Rechaza `branchCode = 28`
- Acepta `gafetes` o `mkt2_gafetes`
- Usa PDO con MySQL/MariaDB
- Hace UPSERT idempotente por `branch_code + badge_id + cycle`
- Usa transaccion completa por lote
- Soporta `X-Sync-Token` de forma configurable

## Estructura

```text
CascoGafetes/
  README.md
  index.php
  .htaccess
  config.example.php
  api/
    gafetes/
      sync.php
  src/
    Database.php
    GafeteValidator.php
    GafeteRepository.php
    GafeteController.php
  migrations/
    001_create_mkt2_gafetes.sql
  tests/
    gafetes_sync_test.php
  docs/
    CONTRATO_API.md
    DESPLIEGUE_HOSTINGER.md
```

## Importante

- No esta en produccion.
- No modifica Desktop.
- No ejecuta SQL por si mismo.
- No incluye credenciales reales.
- El archivo `config.php` real debe crearse fuera de `public_html` o protegerse correctamente.

## Curl de referencia

```bash
curl -X POST \
  "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync" \
  -H "Content-Type: application/json" \
  -H "X-Sync-Token: <TOKEN>" \
  -d '{
    "branchCode":"CV",
    "gafetes":[
      {
        "badgeId":"2121",
        "barcode":"2121",
        "status":"R",
        "cycle":1,
        "taxistaId":null,
        "taxistaName":"",
        "createdAt":"2026-07-18T11:31:18"
      }
    ]
  }'
```

No ejecutarlo hasta contar con el backend PHP real y el acceso a Hostinger.
