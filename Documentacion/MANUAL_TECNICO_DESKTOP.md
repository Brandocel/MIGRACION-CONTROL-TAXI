# Manual tecnico Desktop

Fecha: 2026-06-29.

## Objetivo

`ControlTaxiDesktop` es una aplicacion WPF nativa para Windows que trabaja 100% local con SQLite. No usa navegador, WebView, IIS, localhost, Hostinger ni APIs remotas.

## Proyectos

| Proyecto | Uso |
|---|---|
| `ControlTaxiDesktop` | Aplicacion WPF para usuario final. |
| `ControlTaxiDesktop.Tools` | Importacion, normalizacion, verificacion y reportes tecnicos. |
| `ControlTaxiWeb` | Sistema Web conservado como referencia. No se elimina. |

## Base de datos local

Ruta normal:

```text
DatosLocal\ControlTaxi.db
```

La aplicacion crea automaticamente la carpeta `DatosLocal` y la base SQLite si no existen.

Modo prueba:

```powershell
ControlTaxiDesktop.exe --self-test
```

Ese modo usa `DatosLocal\ControlTaxi.prueba.db` y no debe confundirse con la base real.

## Servicios principales

| Servicio | Responsabilidad |
|---|---|
| `LocalDatabase` | Resuelve ruta SQLite, crea esquema local y configura SQLite. |
| `LocalUserRepository` | Login, usuarios, hash de contrasenas y permisos. |
| `LocalOperationsRepository` | Registros, tarifas, hoteles, taxistas, gafetes, transportes, guias, gastos y relaciones. |
| `LocalPosRepository` | Ventas, pagos, comisiones, cortes, reportes, auditoria y ticket dejada. |
| `DesktopOutputService` | CSV, XLS/XLSX, PDF simple, texto e impresion. |
| `LocalErrorLogger` | Registra errores importantes en `LocalAuditoria`. |

## Seguridad

- SQLite se abre con `ForeignKeys=true`.
- Se usa `ReadWriteCreate`; la base se crea si no existe.
- Contraseñas nuevas se guardan con PBKDF2.
- Permisos por modulo se leen desde `DesktopPermissions` o desde usuarios importados.
- No se guardan claves de SQL Server en el proyecto Desktop.
- Las rutas de exportacion las elige el usuario con `SaveFileDialog`.

## Rendimiento y recursos

- Las conexiones SQLite se abren y cierran con `await using`.
- La base usa `busy_timeout`, WAL y `synchronous=NORMAL`.
- Consultas frecuentes tienen indices base en ventas, pagos, comisiones y auditoria.
- Los reportes trabajan sobre SQLite local.

## Compilacion

```powershell
dotnet build 'CONTROL TAXI.sln' --no-restore
```

## Publicacion

```powershell
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
```

Salida:

```text
ControlTaxiDesktop\bin\Release\net9.0-windows\win-x64\publish
```

## Pruebas tecnicas

```powershell
dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
```

