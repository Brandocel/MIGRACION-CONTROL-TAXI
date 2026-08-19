# Reporte de limpieza para Desktop

Fecha: 26/06/2026

## Archivos y carpetas eliminados

### Lanzadores del sistema web, localhost y red

Se eliminaron porque ninguno es referenciado por `ControlTaxiDesktop` ni por
`ControlTaxiDesktop.Tools`, y todos iniciaban Kestrel, un navegador, `localhost`,
una IP remota o un script externo:

- `Abrir-ControlTaxi-Local.cmd`
- `Abrir-ControlTaxi-Red.cmd`
- `Abrir-ControlTaxi.cmd`
- `Abrir-Puerto-ControlTaxi-5298.cmd`
- `ABRIR_CONTROL_TAXI_DESKTOP.cmd` (a pesar del nombre, invocaba un `.cmd` web en
  otra carpeta)
- `ABRIR_CONTROL_TAXI_FUNCIONA.cmd`
- `ABRIR_CONTROL_TAXI_SERVIDOR.cmd`
- `ABRIR_CONTROL_TAXI_SIN_CONSOLA.vbs`
- `Crear-AccesoDirecto-ControlTaxi.cmd`
- `DIAGNOSTICAR-CONTROL-TAXI-RED.cmd`
- `INICIAR_CONTROL_TAXI_OCULTO.cmd`
- `INICIAR_CONTROL_TAXI_OCULTO.ps1`
- `INICIAR_CONTROL_TAXI_OCULTO.vbs`
- `LEEME-CONTROL-TAXI-RED.txt`
- `Publicar-Para-Servidor.cmd`
- `REPARAR-CONTROL-TAXI-POR-FUERA.cmd`
- `Start-ControlTaxi.ps1`

### Sincronización y mantenimiento remoto

Se retiraron porque su único propósito era operar Hostinger, SQL Server remoto o
tareas de sincronización, todos fuera del diseño offline de Desktop:

- `api-php-hostinger/` completo
- `tools/SyncHostingerPullOnly.ps1`
- `sync-sqlserver-hostinger-hidden.vbs`
- `INTENTAR-BORRAR-HOSTINGER-2026-06-16.cmd`
- `LIMPIAR-DIA-2026-06-16-SERVIDOR.cmd`
- `LIMPIAR-FOLIOS-0090-0091.cmd`

### Artefactos generados

- `bin/` y `obj/` de la aplicación web: binarios y archivos intermedios
  reconstruibles, no código fuente.
- `tools/`: quedó vacía después de retirar el único sincronizador.

## Archivos conservados

| Elemento | Razón |
|---|---|
| `ControlTaxiDesktop/` | Aplicación WPF nativa y ejecutable Windows. |
| `ControlTaxiDesktop.Tools/` | Importador y verificador local de SQLite. |
| `Controllers/`, `Views/`, `Services/`, `Interfaces/`, `Models/`, `Data/`, `Program.cs`, `ControlTaxiWeb.csproj` | Referencia funcional activa para portar cada regla, consulta, reporte, formulario y exportación sin cambiar su comportamiento. Aún existen módulos no migrados a WPF. |
| `PUBLICAR_CONTROL_TAXI/`, `CONTROL_TAXI_SUBIR_SERVIDOR_ACTUALIZADO/` | Copias de publicación existentes; no las eliminé porque pueden ser la única distribución/restauración utilizable del sistema anterior. |
| `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json` | Configuración necesaria para compilar y contrastar el sistema web de referencia mientras continúe la migración. |
| Scripts y SQL de validación restantes | Material de comparación para reportes, usuarios y datos durante la migración. |
| `Fix-ExcelReports.ps1` | Herramienta de reparación de archivos Excel independiente de la red; no hay evidencia suficiente para afirmar que ya no se usa. |

## Dependencias eliminadas

Ninguna todavía. El paquete SQL Server de la aplicación web y sus dependencias
siguen siendo necesarios para compilar la referencia funcional. El proyecto WPF
solo incorpora `Microsoft.Data.Sqlite`; no incluye dependencias web.

## Configuraciones reemplazadas

Ninguna se eliminó todavía. Las configuraciones HTTP y remotas permanecen aisladas
en el proyecto web de referencia. El WPF usa `DatosLocal/ControlTaxi.db` y, para
pruebas sin datos reales, `DatosLocal/ControlTaxi.prueba.db`; no lee
`appsettings.json` ni abre una conexión de red.

## Verificación posterior

`dotnet build ControlTaxiDesktop\\ControlTaxiDesktop.csproj --no-restore` terminó
correctamente, sin advertencias ni errores.
