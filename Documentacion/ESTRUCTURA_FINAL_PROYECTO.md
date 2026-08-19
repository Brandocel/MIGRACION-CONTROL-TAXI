# Estructura final del proyecto

Fecha: 2026-06-29.

## Resumen

Se reorganizo el proyecto para dejar en la raiz solamente lo necesario para trabajar, entregar y mantener la aplicacion Desktop.

No se elimino el sistema Web original: se movio a `Proyecto_Antiguo.zip`.

## Arbol final de carpetas

```text
MIGRACION CONTROL TAXI
|
|-- ControlTaxiDesktop
|   |-- Models
|   |-- Services
|   |-- App.xaml
|   |-- App.xaml.cs
|   |-- MainWindow.xaml
|   |-- MainWindow.xaml.cs
|   |-- OperationsWindow.xaml
|   |-- OperationsWindow.xaml.cs
|   |-- PosWindow.xaml
|   |-- PosWindow.xaml.cs
|   |-- UserAdminWindow.xaml
|   |-- UserAdminWindow.xaml.cs
|   |-- ControlTaxiDesktop.csproj
|   `-- app.manifest
|
|-- ControlTaxiDesktop.Tools
|   |-- Program.cs
|   `-- ControlTaxiDesktop.Tools.csproj
|
|-- DatosLocal
|   `-- ControlTaxi.db
|
|-- Release
|   |-- ControlTaxiDesktop.exe
|   |-- ControlTaxiDesktop.dll
|   |-- ControlTaxiDesktop.deps.json
|   |-- ControlTaxiDesktop.runtimeconfig.json
|   |-- Microsoft.Data.Sqlite.dll
|   |-- SQLitePCLRaw.*
|   |-- e_sqlite3.dll
|   |-- Config
|   `-- DatosLocal
|       `-- ControlTaxi.db
|
|-- Documentacion
|   |-- ARQUITECTURA_DESKTOP.md
|   |-- BACKUP_Y_RECUPERACION.md
|   |-- ENTREGA_FINAL_DESKTOP.md
|   |-- MANUAL_INSTALACION_DESKTOP.md
|   |-- MANUAL_TECNICO_DESKTOP.md
|   |-- MANUAL_USUARIO_DESKTOP.md
|   |-- VALIDACION_FINAL_DESKTOP.md
|   |-- VALIDACION_PARIDAD_DATOS_REALES.md
|   `-- otros reportes historicos de migracion
|
|-- Config
|   |-- appsettings.json
|   `-- appsettings.Development.json
|
|-- Logs
|-- Backup
|-- Proyecto_Antiguo.zip
|-- CONTROL TAXI.sln
`-- .gitignore
```

Nota: `.git` y `.agents` permanecen como carpetas ocultas/metadatos del entorno de trabajo. No forman parte del paquete de entrega al usuario final.

## Explicacion de cada carpeta

| Carpeta / archivo | Proposito |
|---|---|
| `ControlTaxiDesktop` | Codigo fuente WPF nativo de la aplicacion Desktop. |
| `ControlTaxiDesktop.Tools` | Herramientas de importacion, normalizacion, validacion y pipeline SQLite. |
| `DatosLocal` | Base SQLite real local: `ControlTaxi.db`. |
| `Release` | Carpeta portable para ejecutar la aplicacion Desktop en otra computadora. |
| `Documentacion` | Manuales, reportes de migracion, validaciones y documentos tecnicos. |
| `Config` | Configuraciones conservadas. |
| `Logs` | Carpeta preparada para bitacoras externas si se requieren. |
| `Backup` | Carpeta preparada para respaldos de base local. |
| `Proyecto_Antiguo.zip` | Archivo comprimido con el sistema Web original, versiones anteriores, scripts historicos y recursos no actuales. |
| `CONTROL TAXI.sln` | Solucion actual enfocada en `ControlTaxiDesktop` y `ControlTaxiDesktop.Tools`. |

## Que fue movido a `Proyecto_Antiguo.zip`

Se movieron y comprimieron los componentes del Web original y material historico:

- `CONTROL_TAXI_SUBIR_SERVIDOR_ACTUALIZADO`
- `Controllers`
- `ControlTaxiWeb.csproj`
- `Data`
- `Interfaces`
- `Models`
- `Program.cs` del Web
- `Properties`
- `PUBLICAR_CONTROL_TAXI`
- `Services` del Web
- `Views`
- `wwwroot`
- scripts SQL historicos/de validacion:
  - `CrearUsuarioGuadalupe.sql`
  - `MYSQL_VALIDAR_DEJADAS_19_20.sql`
  - `SQL_REPORTE_APPMOVIL_VALIDACION.sql`
  - `SQL_VALIDAR_DEJADAS_19_20.sql`
- script historico:
  - `Fix-ExcelReports.ps1`

El ZIP fue creado correctamente y contiene 1,349 entradas. Despues de verificarlo, se elimino la carpeta `Proyecto_Antiguo` sin comprimir para evitar duplicados.

## Que quedo como proyecto actual

Quedo como proyecto activo:

- `ControlTaxiDesktop`
- `ControlTaxiDesktop.Tools`
- `DatosLocal\ControlTaxi.db`
- `Release`
- `Documentacion`
- `Config`
- `Logs`
- `Backup`
- `CONTROL TAXI.sln`

La solucion fue actualizada para quitar la referencia al proyecto Web movido. Ahora compila solo:

- `ControlTaxiDesktop`
- `ControlTaxiDesktop.Tools`

## Limpieza realizada

Se eliminaron unicamente artefactos regenerables:

- `bin`
- `obj`
- `.vs` si existia
- `TestResults` si existia

Visual Studio y `dotnet restore/build` pueden regenerarlos automaticamente.

## Confirmacion de seguridad

- No se elimino codigo fuente Desktop.
- No se eliminaron archivos `.cs` del Desktop.
- No se eliminaron archivos `.xaml`.
- No se elimino `DatosLocal\ControlTaxi.db`.
- No se elimino la configuracion; se movio a `Config`.
- No se elimino el Web; se comprimio en `Proyecto_Antiguo.zip`.

## Validacion ejecutada despues de reorganizar

Comandos ejecutados:

```powershell
dotnet restore 'CONTROL TAXI.sln'
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\Release\DatosLocal\ControlTaxi.db
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false -o .\Release
.\Release\ControlTaxiDesktop.exe --self-test
```

Resultado:

- Restore: correcto.
- Build: correcto, 0 errores.
- Self-test del proyecto: correcto.
- Validacion de base real en `DatosLocal\ControlTaxi.db`: correcta.
- Validacion de base real copiada a `Release\DatosLocal\ControlTaxi.db`: correcta.
- Publicacion en `Release`: correcta.
- Self-test del ejecutable de `Release`: correcto.

## Confirmacion final

El ejecutable continua funcionando correctamente despues de la reorganizacion.

La aplicacion Desktop encuentra correctamente la base real en:

```text
DatosLocal\ControlTaxi.db
```

y la carpeta portable de entrega contiene su propia copia en:

```text
Release\DatosLocal\ControlTaxi.db
```

La estructura queda lista para comenzar el uso operativo de la aplicacion Desktop.
