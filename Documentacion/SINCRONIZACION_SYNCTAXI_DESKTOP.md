# Sincronización SyncTaxi → Desktop (Hostinger → SQL Server local)

Fecha: 2026-07-09

## Qué hace y por qué

La App móvil envía sus registros a **Hostinger** (API PHP,
`https://lightyellow-porpoise-679527.hostingersite.com`, endpoints
`GET /sync/pull-changes` y `GET /api/taxis/registros`, protegidos con el header
`X-Sync-Token`).

El **Web** lee de Hostinger. El **Desktop** NO lee de Hostinger: lee de
**SQL Server local**, tabla `dbo.AppMovilRegistro`.

El **SyncTaxi** (`sync-sqlserver-hostinger-bidirectional.ps1`) es el
"sincronizador/API": jala los registros de Hostinger y los guarda en SQL Server.
Antes apuntaba a otro servidor, por eso el Desktop no veía los datos. Ahora se
apunta a la base local y el Desktop lo arranca solo.

```
App móvil ──POST──► Hostinger ──(SyncTaxi jala)──► SQL Server dbo.AppMovilRegistro ──► Desktop
```

## Prueba real hecha el 2026-07-09 (verificada)

- Máquina local: **`CONTAHOKA\SQLEXPRESS`** (SQL Server 16), base **`mkt`**,
  **autenticación de Windows** (el usuario `sa` está deshabilitado aquí).
- Se corrió en modo **PullOnly** (solo jala de Hostinger, no escribe en
  producción).
- Resultado: `dbo.AppMovilRegistro` pasó de **478 → 978** registros. ExitCode 0.

## Configuración por archivo `sync.local.config.json`

Vive junto al script. Si existe, sus valores mandan sobre los del `param()`. En
el servidor de producción, si no existe, el script se comporta como siempre.
Opciones:

| Clave | Para qué |
|---|---|
| `SqlServer` | Instancia SQL (ej. `CONTAHOKA\\SQLEXPRESS` o `REYNA`). |
| `IntegratedSecurity` | `true` = autenticación de Windows (ignora usuario/pass). |
| `SqlUser` / `SqlPassword` | Autenticación SQL (solo si `IntegratedSecurity` es false). |
| `MktDatabase` | Base POS (ej. `mkt` o `mkt2`). |
| `ApiBaseUrl` / `SyncToken` | Conexión a Hostinger. |
| `BranchCode` | Sucursal (ej. `28`). |
| `PullOnly` | `true` = solo jala; NO sube gafetes ni marca sincronizado en Hostinger. |

### Perfil LOCAL de prueba (esta máquina, seguro)

```json
{
  "SqlServer": "CONTAHOKA\\SQLEXPRESS",
  "IntegratedSecurity": true,
  "MktDatabase": "mkt",
  "ApiBaseUrl": "https://lightyellow-porpoise-679527.hostingersite.com",
  "SyncToken": "HokaTaxisSync2050",
  "BranchCode": "28",
  "PullOnly": true
}
```

### Perfil PRODUCCIÓN (servidor real)

Quitar `PullOnly` (o ponerlo en `false`) para que vuelva a ser bidireccional, y
ajustar servidor/base/credenciales del servidor real. En producción normalmente
NO se usa este archivo y el script toma sus valores por defecto del `param()`.

## Cómo se integró al Desktop

- **`ControlTaxiDesktop/Services/SyncTaxiLauncher.cs`** — al abrir el Desktop
  dispara el loop oculto del SyncTaxi. No reimplementa la sincronización. Si algo
  falla, el Desktop abre igual. El loop evita procesos duplicados por sí mismo.
- **`ControlTaxiDesktop/App.xaml.cs`** — llama a `SyncTaxiLauncher.TryStart()`.
- **`ControlTaxiDesktop.csproj`** — copia la carpeta `SyncTaxi\` junto al `.exe`.
  (También se suprimió el aviso de auditoría NU1903 —vulnerabilidad transitiva de
  `System.Formats.Asn1` vía `Microsoft.Data.SqlClient 5.1.6`— para que la build no
  se rompa; el arreglo de fondo es subir `Microsoft.Data.SqlClient`.)
- **`ControlTaxiDesktop/SyncTaxi/`** — copia de runtime del sincronizador (4
  archivos) que viaja con el Desktop.

## Config del Desktop hacia la base local

`Config/appsettings.Development.json` se apuntó a la base local (el Desktop
prefiere Development sobre el `appsettings.json` base, que queda como plantilla
de producción):

```
Server=CONTAHOKA\SQLEXPRESS;Database=mkt;Integrated Security=True;...
DatabaseNames: App=ControlTaxis, Compuadmo=compuadmo, Joyeria=joyeria, Pos=mkt
```

Las 4 bases (`mkt`, `ControlTaxis`, `compuadmo`, `joyeria`) se verificaron
alcanzables con autenticación de Windows.

## Cómo probar de nuevo

Manual (una pasada, ver salida):

```powershell
cd "...\SyncTaxi"
powershell -NoProfile -ExecutionPolicy Bypass -File .\sync-sqlserver-hostinger-bidirectional.ps1
```

Debe imprimir `Config local aplicada... (Server=CONTAHOKA\SQLEXPRESS, DB=mkt, IntegratedSecurity=True, PullOnly=True)`
y `Modo PullOnly: ... no se escribe en produccion`. Verifica:

```sql
SELECT COUNT(*) FROM mkt.dbo.AppMovilRegistro;
```

Automático: abre `ControlTaxiDesktop.exe`; al arrancar lanza el loop y los
registros aparecen en Registro App móvil / Relaciones. Log del loop:
`...\SyncTaxi\logs\sync-sqlserver-hostinger-loop.log`.
