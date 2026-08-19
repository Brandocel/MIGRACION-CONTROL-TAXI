# Preparación de respaldos de origen

No ejecute todavía scripts de migración ni borre archivos. Esta guía solo genera
copias de solo lectura para poder comparar la futura aplicación Desktop contra el
sistema actual.

## Carpeta de entrega

En la raíz del proyecto cree esta estructura. Está excluida de Git para que no se
publiquen datos operativos ni contraseñas:

```text
Respaldo_Origen/
  SqlServer/
    mkt.bak
    ControlTaxis.bak
    compuadmo.bak
    joyeria.bak
  Hostinger/
    hostinger-completo.sql
    registros.json                 (comprobación adicional)
    tarifas.json                   (comprobación adicional)
    hoteles.json                   (comprobación adicional)
    manifiesto-hostinger.json      (conteos y fecha de exportación)
  MANIFIESTO-SHA256.txt
```

Los cinco archivos principales son los cuatro `.bak` y `hostinger-completo.sql`.
Los JSON son evidencia adicional de que el contrato que usa el sistema quedó
capturado; no sustituyen el volcado SQL completo.

## 1. SQL Server: de dónde y cómo exportar

Use **SQL Server Management Studio (SSMS)** conectado al mismo servidor que usa
el sistema actual. No exporte tablas a Excel, CSV ni solo el esquema: se requiere
una copia binaria completa, con datos, índices y metadatos.

Para cada base `mkt`, `ControlTaxis`, `compuadmo` y `joyeria`:

1. En SSMS abra **Databases**.
2. Clic derecho sobre la base → **Tasks** → **Back Up...**.
3. Seleccione **Backup type: Full** y **Backup component: Database**.
4. En *Destination* deje solo **Disk** y agregue el archivo indicado en la
   carpeta `Respaldo_Origen\\SqlServer`.
5. En *Media Options*, elija **Back up to a new media set, and erase all existing
   backup sets** únicamente si ese archivo es un respaldo anterior que desea
   reemplazar.
6. En *Backup Options*, active **Verify backup when finished** y, si aparece,
   **Perform checksum before writing to media**.
7. Ejecute y guarde el mensaje de éxito.

Los nombres deben quedar exactamente así: `mkt.bak`, `ControlTaxis.bak`,
`compuadmo.bak`, `joyeria.bak`.

Alternativa por consulta de SSMS (cambie solamente la ruta si fuera necesario):

```sql
BACKUP DATABASE [mkt]
TO DISK = N'C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\Respaldo_Origen\SqlServer\mkt.bak'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, STATS = 10;

BACKUP DATABASE [ControlTaxis]
TO DISK = N'C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\Respaldo_Origen\SqlServer\ControlTaxis.bak'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, STATS = 10;

BACKUP DATABASE [compuadmo]
TO DISK = N'C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\Respaldo_Origen\SqlServer\compuadmo.bak'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, STATS = 10;

BACKUP DATABASE [joyeria]
TO DISK = N'C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\Respaldo_Origen\SqlServer\joyeria.bak'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, STATS = 10;
```

`COPY_ONLY` es obligatorio: crea la copia sin modificar la cadena normal de
respaldos del servidor. La cuenta de SQL Server debe poder escribir en la ruta
indicada; si el servidor remoto no puede verla, genere primero los `.bak` en una
carpeta local **del servidor SQL** y después cópielos a esta carpeta del proyecto.

## 2. Hostinger: de dónde y cómo exportar

La copia correcta debe salir de la **base MySQL/MariaDB de Hostinger**, no de la
pantalla web ni de los endpoints. Ingrese a hPanel → **Databases** → **phpMyAdmin**
y seleccione la base que alimenta la API de taxis.

1. Abra la base completa, no una tabla aislada.
2. Pulse **Export** → método **Custom**.
3. Marque todas las tablas.
4. Formato: **SQL**. Active estructura, datos, `DROP TABLE / VIEW / PROCEDURE`,
   `CREATE TABLE` y `INSERT` extendidos.
5. Compresión: **None** (debe quedar `hostinger-completo.sql`, no `.zip`).
6. Guarde el resultado como
   `Respaldo_Origen\\Hostinger\\hostinger-completo.sql`.

Verifique especialmente que el SQL incluya estas tablas detectadas por el código:

- `mkt2_trip_records` (registros)
- `mkt2_catalog_taxis` (taxistas)
- `mkt2_gafetes` (gafetes)
- `mkt2_service_rates` (tarifas)
- `mkt2_hotels` y/o `mkt2_hoteles` (hoteles)
- `mkt2_dejada_equivalencias` si existe (equivalencias de importe)

Si Hostinger usa procedimientos, vistas o triggers, manténgalos también: phpMyAdmin
debe exportar *routines*, *events* y *triggers* cuando estén disponibles.

## 3. Captura JSON adicional del contrato Hostinger

Mientras el sistema actual sigue en línea, guarde también las respuestas crudas
de sus endpoints. No edite los JSON. Para registros, indique un rango que cubra
todo el histórico y confirme que la API no limita el resultado; si lo limita,
exporte por mes y conserve todos los archivos.

```powershell
$base = 'https://<dominio-hostinger-actual>'
$out = 'C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\Respaldo_Origen\Hostinger'
New-Item -ItemType Directory -Force -Path $out | Out-Null

Invoke-WebRequest "$base/api/taxis/tarifas" -OutFile "$out\tarifas.json"
Invoke-WebRequest "$base/api/taxis/options" -OutFile "$out\hoteles.json"
Invoke-WebRequest "$base/api/taxis/registros?dateFrom=2000-01-01&dateTo=2099-12-31" -OutFile "$out\registros.json"
```

No incluyo el token de sincronización en esta guía. Si la API exige autenticación,
use el mecanismo que ya usa su administrador. Para taxistas no basta el endpoint
`catalogo/buscar`, porque requiere una búsqueda y puede omitir registros: la fuente
de verdad es `mkt2_catalog_taxis` dentro de `hostinger-completo.sql`.

## 4. Validación obligatoria

En SSMS ejecute para cada `.bak`:

```sql
RESTORE VERIFYONLY FROM DISK = N'<ruta-del-archivo.bak>' WITH CHECKSUM;
RESTORE HEADERONLY FROM DISK = N'<ruta-del-archivo.bak>';
```

`VERIFYONLY` debe terminar correctamente y `HEADERONLY` debe mostrar el nombre de
base esperado. Además, restaure cada copia con otro nombre en una instancia de
prueba y ejecute este inventario, guardando el resultado como CSV o texto junto
al respaldo:

```sql
SELECT s.name AS esquema, t.name AS tabla, SUM(p.rows) AS filas
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
```

Para Hostinger, abra `hostinger-completo.sql` y compruebe que contiene tanto
`CREATE TABLE` como `INSERT INTO` para cada tabla listada arriba. Para una
comprobación real, impórtelo a una base MySQL temporal y ejecute:

```sql
SELECT 'mkt2_trip_records' tabla, COUNT(*) filas FROM mkt2_trip_records
UNION ALL SELECT 'mkt2_catalog_taxis', COUNT(*) FROM mkt2_catalog_taxis
UNION ALL SELECT 'mkt2_gafetes', COUNT(*) FROM mkt2_gafetes
UNION ALL SELECT 'mkt2_service_rates', COUNT(*) FROM mkt2_service_rates;
```

Finalmente, desde PowerShell dentro de la raíz del proyecto genere huellas de
integridad. El archivo producido no contiene datos, solo nombres y hashes:

```powershell
Get-ChildItem .\Respaldo_Origen -File -Recurse |
  Get-FileHash -Algorithm SHA256 |
  Format-Table Algorithm, Hash, Path -AutoSize |
  Out-File .\Respaldo_Origen\MANIFIESTO-SHA256.txt -Encoding utf8
```

## 5. Conversión posterior a SQLite

Cuando los archivos anteriores estén completos y validados, el primer paso de la
migración será un importador nuevo y reproducible:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-source `
  --input .\Respaldo_Origen `
  --output .\DatosLocal\ControlTaxi.db `
  --verify
```

Ese proyecto se creará **después** de aceptar los respaldos; actualmente no existe
para evitar una conversión sobre datos incompletos. El comando restaurará los
`.bak` en una instancia temporal local, importará el SQL de Hostinger y generará
conteos/hash de origen y destino. No se conectará a Internet y no modificará los
archivos de respaldo.
