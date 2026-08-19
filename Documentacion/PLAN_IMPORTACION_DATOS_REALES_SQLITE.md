# Plan para importar datos reales a SQLite y validar Desktop offline

Fecha: 26/06/2026.

No se encontró dentro del proyecto una base real `DatosLocal/ControlTaxi.db`, ni
respaldos `.bak`, `.sqlite`, `.db`, `.csv` o `.json` con datos reales. Solo
existen bases temporales `ControlTaxi.prueba.db`. Por eso ningún módulo se
marca como validado con datos reales todavía.

## Objetivo

Crear una base local:

```text
DatosLocal/ControlTaxi.db
```

con datos importados desde copias locales/restauradas del sistema actual, para
probar `ControlTaxiDesktop.exe` totalmente offline.

## Entradas necesarias

Restaurar o tener disponible localmente una copia de:

- `mkt`
- `ControlTaxis`
- `compuadmo`
- `joyeria`
- datos Hostinger en JSON: `registros`, `tarifas`, `hoteles`, `taxistas`,
  `gafetes`

La importación debe hacerse desde bases/restauraciones locales, no desde el
servidor remoto.

## Comandos de importación

Ejecutar desde la raíz del proyecto.

### 1. Crear carpeta destino

```powershell
New-Item -ItemType Directory -Force .\DatosLocal
```

### 2. Importar SQL Server restaurado localmente

Usar una cadena local por cada base. Ejemplos:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver --connection "Server=(localdb)\MSSQLLocalDB;Database=mkt;Trusted_Connection=True;TrustServerCertificate=True" --source-name mkt --output .\DatosLocal\ControlTaxi.db
```

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver --connection "Server=(localdb)\MSSQLLocalDB;Database=ControlTaxis;Trusted_Connection=True;TrustServerCertificate=True" --source-name ControlTaxis --output .\DatosLocal\ControlTaxi.db
```

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver --connection "Server=(localdb)\MSSQLLocalDB;Database=compuadmo;Trusted_Connection=True;TrustServerCertificate=True" --source-name compuadmo --output .\DatosLocal\ControlTaxi.db
```

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver --connection "Server=(localdb)\MSSQLLocalDB;Database=joyeria;Trusted_Connection=True;TrustServerCertificate=True" --source-name joyeria --output .\DatosLocal\ControlTaxi.db
```

### 3. Importar JSON local de Hostinger

Colocar los JSON en:

```text
Respaldo_Origen/Hostinger/
```

y ejecutar:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- import-json --input .\Respaldo_Origen\Hostinger --output .\DatosLocal\ControlTaxi.db
```

### 4. Verificar integridad SQLite

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- verify --output .\DatosLocal\ControlTaxi.db
```

### 5. Normalizar datos POS reales para Desktop

Este paso prepara tablas locales Desktop desde los datos crudos importados y
aplica la fórmula Web de comisiones/cortes:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- normalize-pos --db .\DatosLocal\ControlTaxi.db
```

Genera/actualiza:

- `LocalComisiones`
- `LocalCortes`

### 6. Generar reporte de paridad offline

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db --report .\VALIDACION_PARIDAD_DATOS_REALES.md
```

## Pruebas funcionales Desktop offline

Después de generar `DatosLocal/ControlTaxi.db`:

```powershell
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
```

Abrir:

```text
ControlTaxiDesktop/bin/Release/net9.0-windows/win-x64/publish/ControlTaxiDesktop.exe
```

Validar manualmente por módulo:

| Módulo | Validación contra Web |
|---|---|
| Ventas | Buscar mismos folios/productos, comparar vendedor, cliente, gafete, transporte, pax, efectivo, tarjeta, IVA, total y líneas. |
| Pagos | Mismo folio → mismo total venta, comisión, pagado, saldo, estatus y fechas. |
| Comisiones | Mismo rango → mismas filas, base, deducciones, porcentaje, importe, pago y estatus. |
| Cortes | Misma fecha → efectivo, tarjeta, gastos, comisiones, total día, diferencia, movimientos y cerrado. |
| Reportes | Mismos filtros → mismas filas, totales, columnas y orden. |
| Excel/PDF | Comparar nombres de columnas, títulos, totales, formato y contenido. |
| Usuarios/permisos | Mismo usuario → mismos módulos visibles y login local correcto. |
| Auditoría | Cada acción crítica debe registrar usuario, módulo, acción, folios, importe, equipo y detalle. |
| Gafetes | Mismo gafete/taxista → mismo estado, asignación, regreso y bloqueo. |

## Criterio de validación

Un módulo solo puede pasar a **Validado con datos reales** cuando:

- se probó con `DatosLocal/ControlTaxi.db`, no con `ControlTaxi.prueba.db`;
- los datos fueron importados desde respaldos/restauraciones reales;
- el resultado coincide contra el Web en conteos, folios, importes y estados;
- los archivos Excel/PDF generados contienen la misma información;
- no faltan campos, botones, filtros ni procesos del Web.
