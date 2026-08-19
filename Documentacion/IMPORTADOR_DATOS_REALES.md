# Importador de datos reales a SQLite local

Fecha: 26/06/2026.

El importador queda preparado para recibir datos reales y crear/actualizar:

```text
DatosLocal/ControlTaxi.db
```

No usa internet, Hostinger, navegador, WebView, localhost ni servidor web.

## Formatos soportados

- `.bak` de SQL Server restaurado localmente.
- `.csv`.
- `.xlsx`.
- `.json`.
- `.sqlite` / `.db`.
- SQL Server local/restaurado por cadena de conexión.

## Comandos

### SQL Server local

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver --connection "Server=(localdb)\MSSQLLocalDB;Database=mkt;Trusted_Connection=True;TrustServerCertificate=True" --source-name mkt --output .\DatosLocal\ControlTaxi.db
```

### `.bak`

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-bak --server "(localdb)\MSSQLLocalDB" --bak .\Respaldo_Origen\mkt.bak --database mkt_restore --source-name mkt --output .\DatosLocal\ControlTaxi.db
```

Nota: este comando restaura el `.bak` en una base local temporal con el nombre
indicado en `--database` y luego importa sus tablas a SQLite.

### CSV

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-csv --input .\Respaldo_Origen\CSV --source-name csv --output .\DatosLocal\ControlTaxi.db
```

### XLSX

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-xlsx --input .\Respaldo_Origen\Excel --source-name excel --output .\DatosLocal\ControlTaxi.db
```

### JSON

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-json --input .\Respaldo_Origen\Hostinger --output .\DatosLocal\ControlTaxi.db
```

### SQLite / DB

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlite --input .\Respaldo_Origen\ControlTaxi.sqlite --source-name respaldo --output .\DatosLocal\ControlTaxi.db
```

## Pipeline automático posterior

Después de importar datos reales:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db --report .\REPORTE_IMPORTACION_REAL.md
```

Ejecuta:

- `normalize-pos`;
- `validate-parity`;
- revisión de tablas y conteos;
- revisión de módulos listos para validar;
- reporte final con datos faltantes.

## Reportes generados

- `VALIDACION_PARIDAD_DATOS_REALES.md`
- `REPORTE_IMPORTACION_REAL.md`

## Estado de validación

Mientras no exista `DatosLocal/ControlTaxi.db` con datos reales importados,
ningún módulo se marca como validado con datos reales.

