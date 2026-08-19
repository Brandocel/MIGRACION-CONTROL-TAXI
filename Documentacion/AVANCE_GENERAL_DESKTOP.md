# Avance general Desktop

## Actualizacion 2026-06-29 - Paridad Portal y CSV

Se trabajo exclusivamente sobre Desktop/WPF y SQLite local. No se borro ni retiro ningun archivo Web, no se modifico SQL Server y no se cambiaron datos reales.

Cambios aplicados:

- Se agrego `ControlTaxiDesktop/Models/LocalPortalModels.cs`.
- Se agrego y corrigio `ControlTaxiDesktop/Services/LocalPortalRepository.cs` para leer tablas reales importadas del Web: `remisioM`, `ventascom`, `vendedor`, `Productos`, `caja`, `catrans` y `guias`.
- Se agrego `ControlTaxiDesktop/PortalWindow.xaml` y `ControlTaxiDesktop/PortalWindow.xaml.cs`.
- Se integro el modulo `Portal` al menu WPF usando permiso `Reportes`.
- Se agrego prueba de Portal offline en `ControlTaxiDesktop/App.xaml.cs --self-test`.
- Se agrego exportacion CSV generica en `DesktopOutputService`.
- Se agregaron botones CSV para Pagos, Comisiones, Cortes y Gafetes en `PosWindow`.

Estado:

| Area | Estado |
|---|---|
| Portal Dashboard | Implementado funcionalmente offline. |
| Portal Operations | Implementado funcionalmente offline. |
| Portal Commissions | Implementado funcionalmente offline. |
| Portal Vendors | Implementado funcionalmente offline. |
| Portal Products | Implementado funcionalmente offline. |
| Portal Catalogs | Implementado funcionalmente offline. |
| Portal CreateOperation | Implementado como equivalente local SQLite; no modifica SQL Server. |
| CSV pagos/comisiones/corte/gafetes | Implementado funcionalmente. |
| App movil/API remota | No aplica como comunicacion online en Desktop offline; se conserva alternativa por importacion/local SQLite. |
| Formatos visuales exactos PDF/Excel e impresion fisica | Requieren revision manual contra archivos impresos/exportados del Web e impresora real. |

Validacion ejecutada:

- `dotnet build 'CONTROL TAXI.sln' --no-restore`: correcto, 0 errores.
- `dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test`: correcto.
- `dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db`: correcto.
- `dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db`: correcto.
- `dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false -o .\Release`: correcto.
- `.\Release\ControlTaxiDesktop.exe --self-test`: correcto.

Fecha: 26/06/2026.

No se ha eliminado ni retirado ningún archivo Web.

## Actualización con base real SQLite

Fecha: 26/06/2026 15:30.

`DatosLocal/ControlTaxi.db` ya contiene datos reales importados desde SQL Server local:

- `mkt2` -> `mkt`.
- `compuadmoPlaza` -> `compuadmo`.
- `joyeriaPlaza` -> `joyeria`.
- `ControlTaxis` -> `ControlTaxis`.

Se ejecutó el pipeline offline completo:

- `normalize-pos`: correcto.
- `validate-parity`: correcto.
- conteo de registros: correcto.
- publicación Windows x64: correcta.
- `ControlTaxiDesktop.exe --self-test`: correcto.

## Estado real después de normalizar

| Módulo | Estado con datos reales |
|---|---|
| Ventas | Pendiente por diferencia: origen 9,843 movimientos / 4,499 folios; Desktop 4,499 ventas. Falta resolver si el Web suma filas repetidas o consolida por folio. |
| Pagos | Pendiente por diferencia: total origen efectivo+tarjeta 25,912,396.39 contra Desktop 14,571,957.64. |
| Comisiones | Pendiente por diferencia: `mkt.mov_operacion.comision` suma 517,109.15 contra Desktop 385,020.00; 2,280 folios diferentes. |
| Cortes | Validado con datos reales importados para agregados diarios base: 684 días/cortes y esperado 34,865,759.84. |
| Reportes | Parcial: datos reales disponibles para taxi, dejadas, concentrado, pagos/comisiones y gafetes; pendientes diferencias de ventas/pagos/comisiones. |
| Excel/PDF | Pendiente por comparación de formato contra archivos Web. |
| Usuarios/permisos | Validado con datos reales importados: 2 usuarios y 26 permisos. |
| Auditoría | Validado con datos reales importados: 2,553 registros. |
| Gafetes | Validado con datos reales importados: 555 gafetes distintos origen/Desktop. |

## Corrección aplicada a Ventas, Pagos y Comisiones

Fecha: 26/06/2026 15:40.

Se detectó que `mkt.mov_operacion` tiene 9,843 movimientos pero 4,499 folios
distintos. Desktop conserva una venta por folio, pero ahora suma todos los
movimientos reales asociados al folio.

| Módulo | Estado actualizado |
|---|---|
| Ventas | Validado con datos reales: total origen/Desktop 34,865,759.84; 0 folios faltantes/sobrantes. |
| Pagos | Validado con datos reales: total origen/Desktop 25,912,396.39; 0 folios con diferencia. |
| Comisiones | Validado con datos reales: total origen/Desktop 517,109.15; 0 folios con diferencia. |

Detalle: `DIFERENCIAS_FOLIOS_PAGOS_COMISIONES.md`.

## Estado por módulo

| Módulo | Estado Desktop | Validación con datos reales |
|---|---|---|
| Ventas | Implementado localmente; pendiente ampliar todos los campos Web enriquecidos. | Pendiente |
| Pagos | Implementado localmente; pendiente comparación por folio real. | Pendiente |
| Comisiones | Fórmula Web base migrada desde `mov_operacion/transporte`; pendiente ramas App/Dejadas completas. | Pendiente |
| Cortes | Fórmula Web base migrada desde `mov_operacion` y `ControlTaxis.Cortes`. | Pendiente |
| Reportes especializados | Implementados reportes offline base: taxi, dejadas/concentrado, pagos/comisiones y gafetes. | Pendiente |
| Excel/PDF | Exportación `.xlsx` OpenXML y PDF básico local; pendiente comparación exacta contra Web. | Pendiente |
| Usuarios/permisos | Administración visual WPF, roles, estatus y permisos por módulo. | Pendiente |
| Auditoría | Estructura equivalente a `AuditoriaMovimiento`. | Pendiente |
| Gafetes | Alta/asignación/devolución local e historial importado desde `mkt__dbo__gafete`. | Pendiente |

## Última validación técnica

- Build: correcto, 0 errores.
- Self-test Desktop: correcto.
- Normalize-pos: correcto sobre `DatosLocal/ControlTaxi.db` real.
- Validate-parity: correcto sobre `DatosLocal/ControlTaxi.db` real.
- Run-pipeline: correcto sobre `DatosLocal/ControlTaxi.db` real.
- Publish x64: correcto.
- `ControlTaxiDesktop.exe --self-test`: correcto.

## Importador real preparado

Se agregó soporte para importar:

- `.bak` SQL Server local;
- `.csv`;
- `.xlsx`;
- `.json`;
- `.sqlite` / `.db`;
- SQL Server local/restaurado por cadena de conexión.

Documento:

- `IMPORTADOR_DATOS_REALES.md`

Comando final posterior a importación:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db --report .\REPORTE_IMPORTACION_REAL.md
```

## Pendientes reales

Ya existe `DatosLocal/ControlTaxi.db` con datos reales importados. Los pendientes
actuales no son por falta de base, sino por diferencias detectadas:

- Ventas: revisar folios repetidos en `mkt.mov_operacion` y decidir si Desktop debe consolidar o sumar por movimiento.
- Pagos: cuadrar efectivo/tarjeta contra el comportamiento exacto del Web.
- Comisiones: corregir las 2,280 diferencias contra `mkt.mov_operacion.comision`.
- Excel/PDF: comparar formato contra archivos emitidos por Web.
## Actualizacion final - 2026-06-29

No se elimino ni retiro ningun archivo Web.

### Funcionalidad agregada en esta etapa

1. Registro App movil:
   - Se ubico la logica real en `PosSqlMirrorService.AppMovilSync.cs`, `PosSqlMirrorService.Relaciones.cs` y `PosController.cs`.
   - Se documentaron tablas, flujo y dependencias remotas.
   - En Desktop se usa la informacion importada en SQLite (`mkt__dbo__AppMovilRegistro`, `mkt__dbo__AppMovilRegistroGafetes`, `mkt__dbo__RelacionTicketTaxista`) para consultas/reportes/ticket offline.
   - La sincronizacion con app/API remota queda como no aplicable en modo 100% offline.

2. Dejadas/ticket:
   - Se agrego generacion de ticket de dejada desde SQLite local.
   - Se agregaron botones en WPF para vista/exportacion `.txt` e impresion fisica.
   - El formato replica la estructura de 42 columnas usada en el Web.

3. Cuadre final Excel:
   - Se agrego boton `Cuadre final Excel`.
   - Se agrego exportacion OpenXML multi-hoja:
     - `CUADRE`;
     - `CUADRE dejadas`;
     - `REPORTE HOTELES`;
     - `comisiones`;
     - `CORTE FINAL`.

4. Excel/PDF:
   - Se mejoro `DesktopOutputService` para libros `.xlsx` con multiples hojas.
   - El PDF sigue siendo simple/local; queda sujeto a revision visual si se requiere formato identico al Web.

5. Guias:
   - Pantalla local y tabla SQLite ya existen.
   - La fuente real importada `mkt__dbo__deptoguia` no contiene datos utiles suficientes; queda parcial por datos.

6. Impresion:
   - Flujo preparado con `PrintDialog`.
   - La prueba final depende de impresora real.

### Archivos modificados

| Archivo | Cambio |
|---|---|
| `ControlTaxiDesktop/Models/LocalReportModels.cs` | Modelos para ticket, cuadre resumen y corte final. |
| `ControlTaxiDesktop/Services/DesktopOutputService.cs` | Exportacion OpenXML multi-hoja, exportacion de texto e impresion de texto. |
| `ControlTaxiDesktop/Services/LocalPosRepository.cs` | Consultas de ticket dejada, cuadre final y resumen de camiones desde SQLite. |
| `ControlTaxiDesktop/PosWindow.xaml` | Botones para cuadre final, vista ticket e impresion ticket. |
| `ControlTaxiDesktop/PosWindow.xaml.cs` | Eventos WPF para exportar cuadre final y generar/imprimir ticket. |
| `AUDITORIA_FINAL_MIGRACION.md` | Actualizacion de auditoria final. |
| `VALIDACION_FINAL_DESKTOP.md` | Actualizacion de validacion final. |
| `AVANCE_GENERAL_DESKTOP.md` | Este avance. |
| `PENDIENTES_FINALES_DESKTOP.md` | Nuevo documento de cierre de pendientes. |

### Validacion tecnica ejecutada

| Comando | Resultado |
|---|---|
| `dotnet build 'CONTROL TAXI.sln' --no-restore` | Correcto, 0 errores. |
| `dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test` | Correcto. |
| `dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db` | Correcto. |
| `dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db` | Correcto. |
| `dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false` | Correcto. |
| `ControlTaxiDesktop.exe --self-test` | Correcto. |

### Estado final recomendado

Desktop queda listo para prueba piloto offline con datos reales. No se recomienda retirar el Web hasta completar revision visual/manual de Excel, PDF, impresion fisica y flujo App movil.
