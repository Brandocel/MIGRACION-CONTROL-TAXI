# Guia urgente de Hostinger

Estado: guia de despliegue manual. No ejecutada.

## Recomendacion

Integrar primero en el backend PHP real existente. El endpoint de emergencia `api/gafetes/sync.php` solo debe usarse si el router actual no puede modificarse rapido y de forma segura.

## Pasos

1. Entrar a Hostinger.
2. Abrir Administrador de archivos.
3. Descargar un ZIP completo de `public_html/casco-api`.
4. Identificar `index.php` y `.htaccess` existentes.
5. Crear backup manual de los archivos que se tocaran.
6. Subir solo los archivos nuevos o fusionados:
   - `src/Database.php`
   - `src/GafeteValidator.php`
   - `src/GafeteRepository.php`
   - `src/GafeteController.php`
   - `index.php` solo si se va a fusionar con el router real
   - `.htaccess` solo despues de fusionarlo con el existente
   - opcional de emergencia: `api/gafetes/sync.php`
7. Abrir phpMyAdmin.
8. Revisar si ya existe `mkt2_gafetes`.
9. Si no existe o le faltan columnas, adaptar y ejecutar `migrations/001_create_mkt2_gafetes.sql`.
10. Crear `config.php` real fuera de `public_html` si es posible, o protegerlo adecuadamente.
11. Configurar `SYNC_TOKEN` real y `ENVIRONMENT=production`.
12. Probar primero GET existentes:
   - `/casco-api/api/taxis/registros`
   - `/casco-api/api/taxis/options`
   - `/casco-api/api/taxis/tarifas`
13. Probar el nuevo endpoint con un registro controlado.
14. Repetir el mismo POST para comprobar idempotencia.
15. Activar el cliente local solo al final.

## Fusion recomendada del router

- Si el backend real ya centraliza todas las rutas en `index.php`, fusionar solo el bloque de `POST /api/gafetes/sync`.
- Si el backend real usa otro router, mover la logica del controlador a ese router y conservar las clases de `src/`.
- No reemplazar `index.php` ni `.htaccess` de forma ciega.

## Rollback

1. Restaurar el ZIP de `public_html/casco-api`.
2. Restaurar `index.php` original.
3. Restaurar `.htaccess` original.
4. Deshabilitar el endpoint nuevo.
5. Si se agrego tabla nueva y no hay datos utiles, decidir despues si se conserva o se elimina manualmente.
6. Mantener desactivado el cliente local hasta validar de nuevo.

## Checklist de despliegue

- Backup ZIP descargado
- `index.php` real identificado
- `.htaccess` real identificado
- `config.php` protegido
- `SYNC_TOKEN` configurado
- Tabla `mkt2_gafetes` confirmada o migrada
- GET existentes siguen respondiendo
- POST nuevo responde 200 con payload valido
- Reenvio identico responde `unchanged`
- Cliente local sigue desactivado hasta confirmacion final
