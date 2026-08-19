# Candidatos a eliminación — no eliminar todavía

Este inventario es deliberadamente conservador. Ningún archivo listado aquí se
eliminará hasta que la aplicación WPF haya reemplazado el comportamiento indicado
y haya sido validada con datos locales reales.

| Candidato | Motivo | Condición antes de eliminar |
|---|---|---|
| `bin/`, `obj/`, `ControlTaxiDesktop/bin/`, `ControlTaxiDesktop/obj/`, `ControlTaxiDesktop.Tools/bin/`, `ControlTaxiDesktop.Tools/obj/` | Artefactos generados por compilación; no son código fuente. | La compilación reproducible debe seguir siendo correcta. |
| `PUBLICAR_CONTROL_TAXI/` | Copia publicada del sistema web actual, con binarios y dependencias ASP.NET. | Desktop funcional, publicado y aceptado; confirmar que no es el único instalador de producción. |
| `CONTROL_TAXI_SUBIR_SERVIDOR_ACTUALIZADO/` | Otra copia publicada del sistema web y de sus sincronizadores. | Igual que el punto anterior. |
| `api-php-hostinger/` y `tools/SyncHostingerPullOnly.ps1` | Scripts de sincronización con Hostinger, incompatibles con el objetivo offline. | Importación local validada y todos los módulos móviles ya operando con SQLite. |
| `Abrir-ControlTaxi*.cmd`, `INICIAR_CONTROL_TAXI_*`, `Start-ControlTaxi.ps1`, `Crear-AccesoDirecto-ControlTaxi.cmd`, `Abrir-Puerto-ControlTaxi-5298.cmd`, `DIAGNOSTICAR-CONTROL-TAXI-RED.cmd`, `REPARAR-CONTROL-TAXI-POR-FUERA.cmd` | Inician Kestrel, navegador, localhost o red; no pertenecen al ejecutable WPF. | Todos los módulos WPF migrados y el instalador Desktop disponible. |
| `Properties/launchSettings.json`, `Controllers/`, `Views/`, `wwwroot/`, `Program.cs` web | Dependencias exclusivas de ASP.NET MVC. | Solo cuando WPF replique formularios, reportes, impresión, exportación y autenticación; hoy se conservan. |
| `appsettings*.json` web | Contienen configuración de SQL remoto y Hostinger utilizada por el sistema web aún activo. | Retiro completo de la aplicación web y verificación de que ningún proyecto Desktop los consume. |

## No son candidatos por ahora

`Data/`, `Models/`, `Interfaces/`, `Services/`, los archivos SQL de validación y
los scripts de Excel se conservan: contienen esquema, lógica de negocio, contratos
o referencias para la comparación de resultados durante la migración.
