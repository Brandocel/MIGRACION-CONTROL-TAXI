# ValidaciÃ³n de paridad con datos reales

Fecha: 2026-06-29 14:17:06
Base SQLite: `C:\Users\reyna\Documents\MIGRACION CONTROL TAXI\DatosLocal\ControlTaxi.db`

Este reporte no marca ningÃºn mÃ³dulo como validado si no existen datos reales normalizados y comparaciÃ³n contra Web.

| MÃ³dulo | Tablas origen importadas | Tablas Desktop | Estado |
|---|---|---|---|
| Ventas | mkt__dbo__mov_operacion: 9,843<br>mkt__dbo__operacion: 24,499<br>compuadmo__dbo__Productos: 26,881<br>joyeria__dbo__Productos: 7,158 | LocalProductos: 33,945<br>LocalVentas: 4,499<br>LocalVentaLineas: 12,910 | Implementado; pendiente comparar resultados Web vs Desktop |
| Pagos | mkt__dbo__mov_operacion: 9,843 | LocalPagos: 5,414<br>LocalVentas: 4,499 | Implementado; pendiente comparar resultados Web vs Desktop |
| Comisiones | mkt__dbo__mov_operacion: 9,843<br>mkt__dbo__transporte: 23<br>mkt__dbo__dejadas: 37,799<br>mkt__dbo__RelacionTicketTaxista: 43 | LocalComisiones: 4,499 | Implementado; pendiente comparar resultados Web vs Desktop |
| Cortes | mkt__dbo__mov_operacion: 9,843<br>ControlTaxis__dbo__Cortes: 0 | LocalCortes: 684 | Implementado; pendiente comparar resultados Web vs Desktop |
| Reportes | mkt__dbo__mov_operacion: 9,843<br>mkt__dbo__AppMovilRegistro: 674<br>mkt__dbo__dejadas: 37,799 | LocalVentas: 4,499<br>LocalPagos: 5,414<br>LocalComisiones: 4,499<br>LocalCortes: 684 | Implementado; pendiente comparar resultados Web vs Desktop |
| Excel/PDF | mkt__dbo__mov_operacion: 9,843 | LocalVentas: 4,499<br>LocalPagos: 5,414<br>LocalComisiones: 4,499<br>LocalCortes: 684 | Implementado; pendiente comparar resultados Web vs Desktop |
| Usuarios y permisos | ControlTaxis__dbo__Usuarios: 2<br>ControlTaxis__dbo__UsuarioPermisos: 26 | DesktopUsers: 2<br>DesktopPermissions: 26 | Implementado; pendiente comparar resultados Web vs Desktop |
| AuditorÃ­a | mkt__dbo__AuditoriaMovimiento: 2,553 | LocalAuditoria: 2,553 | Implementado; pendiente comparar resultados Web vs Desktop |
| Gafetes | mkt__dbo__gafete: 282,292<br>mkt__dbo__AppMovilRegistroGafetes: 859 | LocalGafetes: 555 | Implementado; pendiente comparar resultados Web vs Desktop |

## Comparaciones automaticas con datos reales

| Comparacion | Resultado | Estado |
|---|---|---|
| Ventas: movimientos/folios/total | Origen filas: 9,843; folios origen: 4,499; ventas Desktop: 4,499; total origen: 34,865,759.84; total Desktop: 34,865,759.84 | Validado con datos reales importados |
| Ventas: duplicados de folio | `mkt.mov_operacion` trae 5,344 filas adicionales con folios repetidos; Desktop conserva una venta por folio y suma todos sus movimientos reales. | Analizado y corregido |
| Pagos: efectivo+tarjeta | Pagos Desktop: 5,414; total origen: 25,912,396.39; total Desktop: 25,912,396.39 | Validado con datos reales importados |
| Comisiones: `mkt.mov_operacion.comision` vs Desktop | comisiones Desktop: 4,499; suma origen almacenada: 517,109.15; suma Desktop: 517,109.15; folios diferentes: 0 | Validado con datos reales importados |
| Comisiones: baseline Web persistido | `ControlTaxis.Comisiones` esta vacia; se compara contra `mkt.mov_operacion.comision` y la formula migrada. | Pendiente por falta de baseline Web persistido |
| Cortes: dias/esperado | dias origen: 684; cortes Desktop: 684; esperado origen: 34,865,759.84; esperado Desktop: 34,865,759.84 | Validado con datos reales importados |
| Cortes: baseline Web persistido | `ControlTaxis.Cortes` esta vacia; se compara contra agregados reales de `mkt.mov_operacion`. | Pendiente por falta de baseline Web persistido |
| Gafetes: distintivos vigentes | gafetes distintos origen: 555; gafetes Desktop: 555 | Validado con datos reales importados |
| Auditoria: registros | origen: 2,553; Desktop: 2,553 | Validado con datos reales importados |
| Usuarios/permisos | usuarios origen/Desktop: 2/2; permisos origen/Desktop: 26/26 | Validado con datos reales importados |
| Excel/PDF | Se verifican contra las tablas normalizadas (`LocalVentas`, `LocalPagos`, `LocalComisiones`, `LocalCortes`, `LocalGafetes`); falta comparacion visual pixel/formato contra archivos generados por Web. | Pendiente por comparacion de formato |

## Criterio para pasar a validado

Cada mÃ³dulo requiere comparar contra el Web: conteo de filas, folios, importes, saldos, comisiones, cortes, archivos Excel/PDF y auditorÃ­a.
