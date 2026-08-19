# Validación final funcional Desktop

Fecha: 2026-06-29.

Este documento resume la validación final del proyecto WPF Desktop contra el sistema Web conservado como referencia. No se eliminó ni retiró ningún archivo Web.

## Conclusión ejecutiva

El Desktop ya funciona offline con `DatosLocal/ControlTaxi.db` real y los datos críticos de ventas, pagos, comisiones, cortes, gafetes, auditoría y usuarios/permisos cuadran contra las tablas reales importadas.

Sin embargo, todavía no recomiendo marcarlo como reemplazo total de producción del Web hasta completar la revisión manual de:

- formato exacto de Excel/PDF contra archivos emitidos por el Web;
- impresión física/visual;
- recorrido manual de todos los botones, filtros y búsquedas desde la UI;
- pantallas especializadas que en Desktop aún usan una pantalla genérica o equivalente parcial.

Estado final recomendado: **apto para prueba piloto offline controlada**, no todavía para retiro definitivo del Web.

## Validaciones automáticas ejecutadas

Comandos ejecutados correctamente:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
ControlTaxiDesktop.exe --self-test
```

Resultado:

- Build: correcto, 0 errores.
- Pipeline: correcto.
- Validate parity: correcto.
- Publish x64: correcto.
- `.exe --self-test`: correcto.
- SQLite real: `DatosLocal\ControlTaxi.db`.
- No se detectó WebView, navegador, localhost, IIS, Kestrel ni llamadas HTTP en `ControlTaxiDesktop`.

Nota: las cadenas `http://` encontradas en Desktop pertenecen a namespaces XAML/OpenXML, no son llamadas de red.

## Datos reales validados

| Área | Resultado | Estado |
|---|---:|---|
| Movimientos origen `mkt.mov_operacion` | 9,843 | Validado |
| Folios distintos origen | 4,499 | Validado |
| Ventas Desktop | 4,499 | Validado |
| Total ventas origen/Desktop | 34,865,759.84 | Validado |
| Total pagos origen/Desktop | 25,912,396.39 | Validado |
| Total comisiones origen/Desktop | 517,109.15 | Validado |
| Folios faltantes Desktop | 0 | Validado |
| Folios sobrantes Desktop | 0 | Validado |
| Folios con diferencia en ventas | 0 | Validado |
| Folios con diferencia en pagos | 0 | Validado |
| Folios con diferencia en comisiones | 0 | Validado |
| Cortes | 684 | Validado |
| Gafetes distintos | 555 | Validado |
| Auditoría | 2,553 | Validado |
| Usuarios/permisos | 2 usuarios / 26 permisos | Validado |

Detalle por folio:

- `DIFERENCIAS_FOLIOS_PAGOS_COMISIONES.md`

## Comparación Excel/PDF Web vs Desktop

| Exportación Web | Archivo Web / acción | Equivalente Desktop | Estado |
|---|---|---|---|
| Registro diario CSV | `ExportRegistroCsv` / `registro_diario.csv` | Exportación CSV de registros locales | Requiere revisión manual de columnas |
| Registro diario PDF | `ExportRegistroPdf` / `movimientos.pdf` | `ControlTaxi-Ventas.pdf` / PDF genérico | Pendiente por formato |
| Pagos CSV | `ExportPagosCsv` / `pagos.csv` | Botón `Exportar CSV` en Pagos genera `pagos.csv` | Implementado; requiere revisión manual de columnas |
| Pagos PDF | `ExportPagosPdf` / `pagos.pdf` | `ControlTaxi-Pagos.pdf` | Requiere revisión visual |
| Comisiones CSV | `ExportComisionesCsv` / `comisiones.csv` | Botón `Exportar CSV` en Comisiones genera `comisiones.csv` | Implementado; requiere revisión manual de columnas |
| Comisiones PDF | `ExportComisionesPdf` / `comisiones.pdf` | `ControlTaxi-Comisiones.pdf` | Requiere revisión visual |
| Corte CSV | `ExportCorteCsv` / `corte.csv` | Botón `Exportar CSV` en Cortes genera `corte.csv` | Implementado; requiere revisión manual de columnas |
| Corte PDF | `ExportCortePdf` / `corte_yyyyMMdd.pdf` | `ControlTaxi-Cortes.pdf` | Requiere revisión visual |
| Gafetes CSV | `ExportGafetesCsv` / `gafetes.csv` | Botón `Gafetes CSV` genera `gafetes.csv`; también existe `Gafetes.xlsx` | Implementado; requiere revisión manual de columnas |
| Reporte taxi Excel | `reporte_taxi_yyyyMMdd.xlsx` | `Reporte_Taxi.xlsx` | Requiere comparación de layout |
| Control dejadas Excel | `control_dejadas_*.xlsx` | `Control_Dejadas.xlsx` | Requiere comparación de layout |
| Concentrado Excel | `concentrado_general_*.xlsx` | `Concentrado_General.xlsx` | Requiere comparación de layout |
| Cuadre final Excel | `cuadre_final_*.xlsx` | No se confirmó equivalente específico completo | Pendiente |
| Pagos/comisiones Excel | `reporte_pagos_comisiones_yyyyMMdd.xlsx` | `Pagos_Comisiones.xlsx` | Requiere comparación de layout |

Conclusión Excel/PDF: los datos base están disponibles y exportan offline, pero el formato todavía no está confirmado como idéntico al Web.

## Revisión de pantallas principales

| Pantalla / módulo | Evidencia Desktop | Estado |
|---|---|---|
| Login local | Existe autenticación SQLite y permisos locales. Self-test correcto. | Validado automático; requiere prueba manual con usuarios reales |
| Menú por permisos | Usa `DesktopPermissions` / `ControlTaxis__dbo__UsuarioPermisos`. | Validado automático |
| Registro diario | Pantalla WPF de registros con alta y consulta. | Requiere revisión manual |
| Tarifas | Pantalla WPF de alta/consulta. | Requiere revisión manual |
| Hoteles | Pantalla WPF de alta/consulta. | Requiere revisión manual |
| Taxistas | Pantalla WPF de alta/consulta. | Requiere revisión manual |
| Gafetes | Alta/asignación/devolución y datos reales normalizados. | Validado datos; requiere impresión/exportación visual |
| Ventas | Totales reales cuadran contra origen. | Validado datos; requiere prueba manual de botones |
| Pagos | Totales reales cuadran contra origen. | Validado datos; requiere prueba manual de registro de pago |
| Comisiones | Totales reales cuadran contra origen. | Validado datos; requiere prueba manual de abono/recalcular |
| Cortes | Totales reales cuadran contra origen. | Validado datos; requiere prueba manual de cierre/impresión |
| Reportes especializados | Consultas offline existen para taxi, dejadas, concentrado, pagos/comisiones y gafetes. | Requiere revisión manual de columnas/formato |
| Usuarios y permisos | Pantalla WPF visual de usuarios/permisos. | Validado datos; requiere prueba manual de bloqueo por usuario |
| Auditoría | 2,553 registros importados y consulta WPF. | Validado datos |

## Módulos Web vs Desktop

| Módulo Web | Estado Desktop | Clasificación |
|---|---|---|
| Registro diario | Implementado en WPF | Requiere revisión manual |
| Registro aplicación móvil / relaciones | Parcial en datos/reportes; no se confirmó pantalla especializada idéntica | Pendiente |
| Ventas | Implementado con SQLite local y totales reales validados | Validado |
| Pagos | Implementado con SQLite local y totales reales validados | Validado |
| Comisiones | Implementado con SQLite local y totales reales validados | Validado |
| Cortes | Implementado con SQLite local y totales reales validados | Validado |
| Reportes | Implementado parcial/offline | Requiere revisión manual |
| Exportaciones Excel/PDF | Implementado parcial/offline | Pendiente por formato |
| Gafetes | Implementado y datos validados | Requiere revisión manual |
| Usuarios/permisos | Implementado y datos validados | Validado |
| Auditoría | Implementado y datos validados | Validado |
| Transportes | Menú existe; pantalla especializada completa no confirmada | Pendiente |
| Guías | Menú existe; pantalla especializada completa no confirmada | Pendiente |
| Gastos | Menú existe; pantalla especializada completa no confirmada | Pendiente |
| Impresión | Impresión básica de reportes existe | Requiere revisión manual en impresora |

## Checklist final

| Módulo | Estado final |
|---|---|
| Ventas | Validado |
| Pagos | Validado |
| Comisiones | Validado |
| Cortes | Validado |
| Usuarios y permisos | Validado |
| Auditoría | Validado |
| Gafetes | Requiere revisión manual |
| Reporte taxi | Requiere revisión manual |
| Dejadas | Requiere revisión manual |
| Concentrado | Requiere revisión manual |
| Pagos/comisiones Excel | Requiere revisión manual |
| Cuadre final Excel | Pendiente |
| Excel/PDF formato idéntico Web | Pendiente |
| Impresión | Requiere revisión manual |
| Transportes | Pendiente |
| Guías | Pendiente |
| Gastos | Pendiente |
| Relaciones ticket-taxista / App móvil | Pendiente |

## Prueba offline

Resultado automático:

- El Desktop trabaja contra SQLite local.
- El `.exe --self-test` abre y cierra sin requerir red.
- `ControlTaxiDesktop` no contiene llamadas HTTP, WebView, navegador, localhost ni servidor web.

Limitación:

- No se realizó una desconexión física de red desde este entorno.
- La prueba visual completa de botones, filtros, impresión y exportaciones requiere interacción humana con la UI publicada.

## Decisión de producción

El Desktop **no debe retirar todavía el Web**.

Puede usarse para una prueba piloto offline controlada de:

- consulta de ventas;
- pagos;
- comisiones;
- cortes;
- auditoría;
- usuarios/permisos;
- gafetes;
- reportes base.

Antes de producción total faltan exactamente:

1. Comparar visualmente cada Excel/PDF contra el archivo Web equivalente.
2. Completar o confirmar equivalentes WPF para transportes, guías, gastos, relaciones/App móvil y cuadre final.
3. Probar manualmente botones, filtros, búsquedas, impresión y exportación desde el `.exe`.
4. Validar bloqueo de permisos con usuarios reales.
5. Ejecutar una prueba operativa completa en una PC sin red física o con adaptadores deshabilitados.
## Actualizacion final de cierre funcional - 2026-06-29

Se implementaron los pendientes finales solicitados sin eliminar ni retirar archivos Web.

### Nuevas validaciones automaticas ejecutadas

```powershell
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
ControlTaxiDesktop.exe --self-test
```

Resultado:

- Build completo: correcto, 0 errores.
- Self-test WPF por `dotnet run`: correcto.
- `validate-parity`: correcto, reporte actualizado.
- `run-pipeline`: correcto, normalizacion real terminada con 9,843 movimientos y 684 cortes.
- Publish Windows x64: correcto.
- `ControlTaxiDesktop.exe --self-test`: correcto.

### Checklist actualizado

| Pendiente final | Resultado |
|---|---|
| Registro App movil | Logica Web localizada y documentada; datos App importados se usan offline. Comunicacion movil/API remota queda como no aplicable offline o pendiente de integracion local futura. |
| Dejadas/ticket | Implementado ticket local imprimible/exportable en formato texto compatible con estructura Web. |
| Cuadre final Excel | Implementado boton y exportacion `.xlsx` multi-hoja offline. |
| Excel/PDF | Mejorada exportacion OpenXML para soportar libros multi-hoja; PDF sigue como formato simple y requiere revision visual si se exige identidad pixel-perfect. |
| Guias | Pantalla y tabla local existen; la fuente real importada no contiene datos utiles suficientes, queda parcial por falta de datos. |
| Impresion fisica | Flujo WPF preparado con `PrintDialog`; prueba final depende de impresora real. |

### Decision actual

El Desktop queda mas cerca de reemplazo funcional, pero todavia recomiendo **prueba piloto controlada** antes de produccion total por estos puntos manuales:

1. Comparar visualmente el Excel de cuadre final Desktop contra un Excel Web real.
2. Imprimir ticket de dejada en la impresora fisica real y ajustar margenes/tamano si hace falta.
3. Validar manualmente botones/filtros en pantallas con usuarios reales.
4. Confirmar si el flujo App movil debe quedarse como consulta offline o si se requiere una futura app/sync local.
## Actualizacion 2026-06-29 - Validacion posterior a Portal/CSV

Se agrego y valido tecnicamente el modulo `Portal` en WPF y los CSV especificos de Pagos, Comisiones, Cortes y Gafetes. La validacion automatica posterior fue correcta:

- Build: correcto, 0 errores.
- Self-test Desktop: correcto, incluyendo flujo Portal offline.
- Validate parity: correcto.
- Run-pipeline: correcto.
- Publish x64 a `Release`: correcto.
- `Release/ControlTaxiDesktop.exe --self-test`: correcto.

Estado actualizado de brechas antes marcadas como ausentes:

| Elemento | Estado actualizado |
|---|---|
| Portal Dashboard / Operations / Commissions / Vendors / Products / Catalogs | Implementado funcionalmente en `PortalWindow` con SQLite local. |
| Portal CreateOperation | Implementado como alta local offline en `LocalVentas`, `LocalRegistros` y `LocalAuditoria`. |
| Pagos CSV | Implementado funcionalmente con boton `Exportar CSV`. |
| Comisiones CSV | Implementado funcionalmente con boton `Exportar CSV`. |
| Corte CSV | Implementado funcionalmente con boton `Exportar CSV`. |
| Gafetes CSV | Implementado funcionalmente con boton `Gafetes CSV`. |

Pendientes que siguen requiriendo revision manual, no automatica: equivalencia visual exacta PDF/Excel, prueba con impresora fisica y prueba por usuario real de bloqueo por boton/accion.
