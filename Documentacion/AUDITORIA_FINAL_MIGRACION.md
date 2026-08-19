# Auditoría final de migración Web a Desktop

Fecha: 2026-06-29.

## Actualizacion de paridad aplicada en esta etapa

No se elimino ningun archivo Web y no se modifico SQL Server. El Web permanece como referencia dentro de `Proyecto_Antiguo.zip` y `Backup/Proyecto_Antiguo_Referencia`.

Estado actualizado: migracion funcional avanzada, con Portal local agregado y exportaciones CSV pendientes cerradas a nivel funcional. Todavia no se debe retirar definitivamente el Web hasta completar revision visual/manual de formatos, impresion fisica y paridad por boton/permisos finos.

| Modulo / funcion | Estado actualizado | Evidencia Desktop |
|---|---|---|
| Portal / Dashboard | ✅ Migrado funcionalmente | `PortalWindow` consulta SQLite local con `LocalPortalRepository.GetDashboardAsync` usando tablas Web importadas `remisioM`/`caja`. |
| Portal / Operations | ✅ Migrado funcionalmente | `PortalWindow` muestra operaciones por fecha/busqueda desde `compuadmo__dbo__remisioM` y `joyeria__dbo__remisioM`. |
| Portal / Commissions | ✅ Migrado funcionalmente | Consulta `ventascom` si existe; si no existe usa `LocalComisiones` normalizada. |
| Portal / Vendors | ✅ Migrado funcionalmente | Consulta `vendedor` local importado por base. |
| Portal / Products | ✅ Migrado funcionalmente | Consulta `Productos` local importado por base. |
| Portal / Catalogs | ✅ Migrado funcionalmente | Consulta `guias` y `catrans`; usa catalogos locales como respaldo offline. |
| Portal / CreateOperation | ✅ Migrado como equivalente offline | Alta local escribe en `LocalVentas`, `LocalRegistros` y `LocalAuditoria`. No modifica remisiones SQL Server porque Desktop es solo SQLite/offline. |
| ExportPagosCsv | ✅ Migrado funcionalmente | Boton `Exportar CSV` en Pagos genera `pagos.csv`. |
| ExportComisionesCsv | ✅ Migrado funcionalmente | Boton `Exportar CSV` en Comisiones genera `comisiones.csv`. |
| ExportCorteCsv | ✅ Migrado funcionalmente | Boton `Exportar CSV` en Cortes genera `corte.csv`. |
| ExportGafetesCsv | ✅ Migrado funcionalmente | Boton `Gafetes CSV` genera `gafetes.csv`. |
| App movil online/API remota | ⚪ No aplica para Desktop offline | La comunicacion en vivo con movil/Hostinger/API no puede ejecutarse sin red; el equivalente local es importar/normalizar datos a SQLite y operar offline. |
| Impresion fisica | Requiere revision manual | El flujo WPF llama a `PrintDialog`; la prueba final depende de impresora real instalada. |
| Formatos visuales Excel/PDF exactos | Requiere revision manual | Existen exportaciones funcionales, pero la equivalencia pixel/formato contra navegador requiere comparar archivos reales de referencia. |

Este documento inventaría el sistema Web y el sistema Desktop, compara sus funcionalidades y deja el estado real de migración. No se eliminó ni retiró ningún archivo Web.

## Resumen ejecutivo

La migración Desktop WPF + SQLite ya cubre los flujos críticos con datos reales:

- ventas;
- pagos;
- comisiones;
- cortes;
- auditoría;
- usuarios/permisos;
- gafetes base;
- importación SQL Server/CSV/XLSX/JSON/SQLite;
- normalización offline;
- validación de paridad.

Durante esta auditoría se detectó que el Desktop listaba módulos Web como Transportes, Guías, Gastos y Relaciones, pero no tenía pantalla propia para ellos. Se implementaron pantallas WPF y tablas SQLite locales para esos módulos, y se agregó normalización desde datos reales cuando existen.

Estado final: **migración funcional avanzada, apta para prueba piloto offline controlada; no lista todavía para retirar definitivamente el Web**.

## Inventario del sistema Web

### Controladores

| Controlador | Funciones principales | Estado Desktop |
|---|---|---|
| `PortalController` | Dashboard, operaciones, comisiones, vendedores, productos, catálogos, crear operación. | ✅ Migrado funcionalmente en `PortalWindow` con SQLite local; `CreateOperation` escribe equivalente offline en tablas locales. |
| `PosController` | Login, dashboard POS, registro, registro app móvil, ventas, pagos, gastos, cortes, comisiones, catálogos, gafetes, relaciones, reportes, usuarios, exportaciones. | Migrado parcialmente. Flujos críticos migrados; algunas pantallas especializadas siguen parciales. |

### Vistas Web

| Vista | Funcionalidad | Estado Desktop |
|---|---|---|
| `Views/Portal/Dashboard.cshtml` | Resumen portal. | Pendiente / no equivalente visual idéntico. |
| `Views/Portal/Operations.cshtml` | Operaciones portal. | Parcial. |
| `Views/Portal/Commissions.cshtml` | Comisiones portal. | Parcial; comisiones POS sí validadas. |
| `Views/Portal/Vendors.cshtml` | Vendedores. | Parcial vía taxistas/guías. |
| `Views/Portal/Products.cshtml` | Productos. | Migrado en POS Desktop. |
| `Views/Portal/Catalogs.cshtml` | Catálogos portal. | Parcial. |
| `Views/Portal/CreateOperation.cshtml` | Alta de operación portal. | Parcial vía registros/ventas locales. |
| `Views/Pos/Auth/Login.cshtml` | Login Web. | Migrado a login WPF local. |
| `Views/Pos/Dashboard/Index.cshtml` | Dashboard POS. | Parcial; Desktop tiene menú por permisos, no dashboard idéntico. |
| `Views/Pos/Registro/Index.cshtml` | Registro diario, filtros, exportación. | Migrado parcialmente. |
| `Views/Pos/RegistroApp/Index.cshtml` | Registro App móvil. | Pendiente parcial; datos importados existen, pantalla idéntica no. |
| `Views/Pos/Ventas/Index.cshtml` | Ventas POS, productos, cobro. | Migrado; totales reales validados. |
| `Views/Pos/Ventas/RemisionDetalle.cshtml` | Detalle remisión. | Pendiente visual específico. |
| `Views/Pos/Ventas/RemisionProductos.cshtml` | Productos de remisión. | Pendiente visual específico. |
| `Views/Pos/Pagos/Index.cshtml` | Pagos de comisiones. | Migrado; totales reales validados. |
| `Views/Pos/Gastos/Index.cshtml` | Gastos. | Implementado en esta etapa como WPF básico/offline. |
| `Views/Pos/Cortes/Index.cshtml` | Cortes y cierre. | Migrado; totales reales validados. |
| `Views/Pos/Comisiones/Index.cshtml` | Comisiones, abonos, recalcular. | Migrado; totales reales validados. |
| `Views/Pos/Catalogos/Index.cshtml` | Transportes, guías, taxistas. | Transportes y guías implementados básico/offline; taxistas ya existía. |
| `Views/Pos/Gafetes/Index.cshtml` | Gafetes, asignación/devolución. | Migrado parcialmente; datos validados, diseño/impresión especializado pendiente. |
| `Views/Pos/Relaciones/Index.cshtml` | Relación ticket-taxista/dejadas. | Implementado en esta etapa como WPF básico/offline. Lógica App móvil avanzada pendiente. |
| `Views/Pos/Relaciones/DejadaTicket.cshtml` | Ticket de dejada/imprimir. | Pendiente formato/impresión. |
| `Views/Pos/Reportes/Index.cshtml` | Reportes especializados y exportaciones. | Parcial; datos disponibles, formatos exactos pendientes. |
| `Views/Pos/Usuarios/Index.cshtml` | Usuarios y permisos. | Migrado a WPF. |
| `Views/Pos/Gafetes/Index.cshtml` | Gestión gafetes. | Parcial; consulta/asignación local existe. |

### Modelos Web

| Grupo/modelo | Uso | Estado Desktop |
|---|---|---|
| `PortalViewModels.cs` | Portal administrativo, dashboard, operaciones, catálogos. | Parcial. |
| `PosCatalogoViewModel` | Transportes, guías, taxistas. | Parcial; Transportes/Guías/Taxistas con modelos Desktop. |
| `PosComisionesViewModel` | Comisiones y pagos. | Migrado funcionalmente para datos reales base. |
| `PosCorteViewModel` | Corte diario. | Migrado. |
| `PosDashboardViewModel` | Dashboard POS. | Parcial. |
| `PosGafetesViewModel`, `PosGafeteRowViewModel` | Gafetes. | Parcial. |
| `PosGastosViewModel` | Gastos. | Implementado básico en esta etapa. |
| `PosOperacionRowViewModel` | Registro/reportes taxi. | Parcial; reportes offline existen. |
| `PosPagosViewModel`, `PosPagoRowViewModel` | Pagos. | Migrado. |
| `PosRegistroAppViewModel` y catálogos App | Registro App móvil. | Pendiente parcial. |
| `PosRegistroDiarioViewModel` | Registro diario. | Parcial. |
| `PosRelacionesViewModel` | Relaciones/dejadas. | Implementado básico; lógica avanzada pendiente. |
| `PosReportesViewModel` | Reportes. | Parcial. |
| `PosUsuariosViewModel` | Usuarios/permisos. | Migrado. |
| `PosVentasViewModel`, `PosVentaDetalleViewModel` | Ventas/productos/remisión. | Migrado parcialmente; totales reales validados. |

### Servicios Web

| Servicio | Funcionalidad | Estado Desktop |
|---|---|---|
| `AppTaxiApiClient` | API externa/App Taxi. | No aplica en modo offline; debe reemplazarse por datos SQLite importados. |
| `PortalService` | Portal de remisiones, vendedores, productos, catálogos. | ✅ Migrado funcionalmente en `LocalPortalRepository` usando tablas importadas `remisioM`, `ventascom`, `vendedor`, `Productos`, `caja`, `catrans` y `guias`. |
| `PosSqlMirrorService.Auth` | Login y permisos. | Migrado. |
| `PosSqlMirrorService.Auditoria` | Auditoría movimiento. | Migrado. |
| `PosSqlMirrorService.Catalogos` | Transportes, guías, taxistas. | Parcial; transportes/guías/taxistas locales implementados, lógica completa Web pendiente. |
| `PosSqlMirrorService.Comisiones` | Cálculos, pagos, ramas App/Dejadas, control de pagos. | Migrado para totales reales base; ramas avanzadas requieren revisión continua. |
| `PosSqlMirrorService.Cortes` | Corte diario/cierre. | Migrado para datos reales base. |
| `PosSqlMirrorService.Gafetes*` | Gafetes, sync, helpers. | Parcial. |
| `PosSqlMirrorService.Gastos` | Gastos. | Implementado básico/offline en esta etapa. |
| `PosSqlMirrorService.Pagos` | Pagos de comisiones. | Migrado. |
| `PosSqlMirrorService.Registro` | Registro diario. | Parcial. |
| `PosSqlMirrorService.Relaciones` | Relaciones, dejadas, ticket, App móvil. | Parcial; pantalla básica implementada. |
| `PosSqlMirrorService.Ventas*` | Ventas, cobro, remisión, helpers. | Migrado parcialmente; totales reales validados. |
| `PosSqlMirrorService.AppMovilSync` | Registro App móvil y sincronización. | Pendiente parcial; APIs remotas no aplican offline. |
| `PosModuleServices` | Fachadas por módulo. | Reemplazadas por repositorios locales Desktop. |

### Procedimientos, consultas SQL y esquemas

| Archivo / área | Contenido | Estado Desktop |
|---|---|---|
| `Data/PosTritonSchema.sql` | Tablas Ventas, Pagos, Comisiones, Transportes, Guías, Taxistas, Gafetes, Gastos, Relaciones, Cortes, Auditoría, Usuarios. | Migrado parcialmente a SQLite local; se agregaron tablas faltantes en esta etapa. |
| `Data/AppMovilMkt2Schema.sql` | Tablas AppMovil, folios, gafetes, tickets, payout. | Parcial; datos importados, UI/lógica App completa pendiente. |
| Consultas `mov_operacion` | Ventas, pagos, comisiones, cortes. | Migrado y validado con datos reales. |
| Consultas `gafete` | Historial y estado gafetes. | Parcial validado por conteo. |
| Consultas `RelacionTicketTaxista` | Relaciones ticket/taxista. | Implementado básico; 43 relaciones importadas. |
| Consultas `transporte` | Catálogo transporte y reglas comisión. | Implementado básico; 23 transportes importados. |
| Consultas `ingresos` | Gastos/ingresos. | Implementado básico; 313 registros importados. |

### Reportes/exportaciones Web

| Reporte/exportación | Estado Desktop |
|---|---|
| Registro CSV/PDF | Parcial; CSV/PDF genérico, formato exacto pendiente. |
| Pagos CSV/PDF | ✅ CSV y PDF disponibles; pendiente solo revisión visual/manual de formato PDF. |
| Comisiones CSV/PDF | ✅ CSV y PDF disponibles; pendiente solo revisión visual/manual de formato PDF. |
| Corte CSV/PDF | ✅ CSV y PDF disponibles; pendiente solo revisión visual/manual de formato PDF. |
| Gafetes CSV | ✅ CSV y XLSX disponibles; pendiente solo revisión manual de columnas/layout. |
| Reporte taxi Excel | Disponible en Desktop, formato exacto pendiente. |
| Dejadas Excel | Disponible en Desktop, formato exacto pendiente. |
| Concentrado Excel | Disponible en Desktop, formato exacto pendiente. |
| Cuadre final Excel | Pendiente específico. |
| Pagos/comisiones Excel | Disponible en Desktop, formato exacto pendiente. |

### APIs, jobs y procesos automáticos

| Elemento | Estado Desktop |
|---|---|
| APIs Hostinger/App Taxi | No aplica directo; deben reemplazarse por SQLite local/importación. |
| Sincronización App móvil | Parcial: datos importados; flujo online no aplica en Desktop offline. |
| Importador SQL Server/CSV/XLSX/JSON/SQLite | Implementado en `ControlTaxiDesktop.Tools`. |
| `normalize-pos` | Implementado. |
| `validate-parity` | Implementado. |
| `run-pipeline` | Implementado. |
| Auditoría local | Implementada. |

## Inventario del sistema Desktop

### Proyectos

| Proyecto | Función |
|---|---|
| `ControlTaxiDesktop` | Aplicación WPF nativa Windows. |
| `ControlTaxiDesktop.Tools` | Importación, normalización y validación SQLite. |

### Ventanas Desktop

| Ventana | Funcionalidad |
|---|---|
| `MainWindow` | Login local, menú por permisos, apertura de módulos. |
| `OperationsWindow` | Registros, tarifas, hoteles, taxistas, gafetes, transportes, guías, gastos, relaciones y reportes básicos. |
| `PosWindow` | Ventas, pagos, comisiones, cortes, reportes especializados, auditoría. |
| `UserAdminWindow` | Usuarios, roles y permisos. |

### Repositorios/servicios Desktop

| Servicio | Funcionalidad |
|---|---|
| `LocalDatabase` | Resolución de SQLite real/prueba, creación de esquema local. |
| `LocalUserRepository` | Login, usuarios, permisos. |
| `LocalOperationsRepository` | Registros, tarifas, hoteles, taxistas, gafetes, transportes, guías, gastos, relaciones. |
| `LocalPosRepository` | Productos, ventas, pagos, comisiones, cortes, reportes, auditoría. |
| `DesktopOutputService` | CSV, Spreadsheet XML, OpenXML, PDF simple, impresión básica. |
| `ControlTaxiDesktop.Tools/Program.cs` | Importador y pipeline. |

### Tablas SQLite Desktop

| Tabla | Estado |
|---|---|
| `DesktopUsers` | Migrada/validada. |
| `DesktopPermissions` | Migrada/validada. |
| `LocalProductos` | Migrada. |
| `LocalVentas` | Migrada/validada. |
| `LocalVentaLineas` | Migrada. |
| `LocalPagos` | Migrada/validada. |
| `LocalComisiones` | Migrada/validada. |
| `LocalCortes` | Migrada/validada. |
| `LocalAuditoria` | Migrada/validada. |
| `LocalGafetes` | Migrada/parcial. |
| `LocalRegistros` | Migrada/parcial. |
| `LocalTarifas` | Migrada/parcial. |
| `LocalHoteles` | Migrada/parcial. |
| `LocalTaxistas` | Migrada/parcial. |
| `LocalTransportes` | Implementada en esta auditoría; 23 importados. |
| `LocalGuias` | Implementada en esta auditoría; sin datos útiles importados. |
| `LocalGastos` | Implementada en esta auditoría; 313 importados. |
| `LocalRelaciones` | Implementada en esta auditoría; 43 importadas. |

## Funciones implementadas en esta auditoría

Se implementó automáticamente:

1. Tablas SQLite locales:
   - `LocalTransportes`
   - `LocalGuias`
   - `LocalGastos`
   - `LocalRelaciones`
2. Modelos Desktop:
   - `LocalTransport`
   - `LocalGuide`
   - `LocalExpense`
   - `LocalRelation`
3. Repositorio local:
   - consulta/guardado de transportes;
   - consulta/guardado de guías;
   - consulta/guardado de gastos;
   - consulta/guardado de relaciones.
4. Pestañas WPF:
   - Transportes;
   - Guías;
   - Gastos;
   - Relaciones.
5. Apertura correcta desde menú por módulo.
6. Normalización en pipeline desde:
   - `mkt__dbo__transporte` -> `LocalTransportes`;
   - `mkt__dbo__ingresos` -> `LocalGastos`;
   - `mkt__dbo__RelacionTicketTaxista` -> `LocalRelaciones`.

## Comparación funcional final

| Función | Estado |
|---|---|
| Login local | Ya migrado |
| Permisos | Ya migrado |
| Usuarios visual | Ya migrado |
| Ventas | Ya migrado |
| Pagos | Ya migrado |
| Comisiones | Ya migrado |
| Cortes | Ya migrado |
| Auditoría | Ya migrado |
| Productos | Migrado parcialmente |
| Registro diario | Migrado parcialmente |
| Registro App móvil | Pendiente de migrar completo |
| Relaciones | Migrado parcialmente; pantalla básica implementada |
| Dejadas/ticket | Migrado parcialmente; impresión/ticket pendiente |
| Gafetes | Migrado parcialmente |
| Transportes | Migrado parcialmente; pantalla básica implementada |
| Guías | Migrado parcialmente; falta fuente real útil |
| Taxistas | Migrado parcialmente |
| Hoteles | Migrado parcialmente |
| Tarifas | Migrado parcialmente |
| Gastos | Migrado parcialmente; pantalla básica implementada |
| Reporte taxi | Migrado parcialmente |
| Reporte dejadas | Migrado parcialmente |
| Concentrado | Migrado parcialmente |
| Cuadre final | Pendiente |
| Pagos/comisiones Excel | Migrado parcialmente |
| Excel/PDF exactos | Pendiente de formato |
| Impresión | Parcial; requiere prueba manual |
| APIs remotas | No aplica para Desktop offline |
| Sync App móvil online | No aplica directo; pendiente equivalente offline completo |

## Riesgos encontrados

1. El Web contiene lógica extensa de App móvil, Hostinger/API y sincronización que no debe ejecutarse en Desktop offline. Se requiere definir sustituto 100% local para el flujo operativo App móvil.
2. Varias exportaciones Desktop generan archivos válidos, pero no tienen aún formato idéntico hoja por hoja al Web.
3. `ControlTaxis.Ventas`, `ControlTaxis.Pagos`, `ControlTaxis.Comisiones` y `ControlTaxis.Cortes` están vacías; la validación real se hizo contra `mkt.mov_operacion`.
4. Guías no importó datos útiles desde la fuente real disponible.
5. Impresión requiere prueba manual con impresora real.
6. La pantalla Desktop de catálogos nuevos es funcional básica, no copia todavía todas las validaciones/estética del Web.

## Validación técnica posterior a implementación

Ejecutado:

```powershell
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
```

Resultado:

- Build correcto, 0 errores.
- Pipeline correcto.
- `normalize-pos`: OK.
- Nuevos conteos normalizados:
  - `LocalTransportes`: 23.
  - `LocalGastos`: 313.
  - `LocalRelaciones`: 43.
  - `LocalGuias`: 0.

## Recomendaciones antes de producción

1. No retirar todavía el Web.
2. Hacer prueba manual de cada pestaña WPF nueva.
3. Comparar Excel/PDF contra archivos Web reales.
4. Implementar o descartar formalmente el flujo completo de Registro App móvil para operación offline.
5. Completar Cuadre final.
6. Probar impresión física.
7. Validar Guías con una fuente real correcta.
8. Ejecutar piloto offline controlado antes de producción.

## Dictamen

La migración funcional **no está completa al 100%** porque aún existen funciones Web pendientes o parciales:

- Registro App móvil completo.
- Dejadas/ticket con impresión idéntica.
- Cuadre final Excel.
- Formato exacto Excel/PDF.
- Guías con datos reales útiles.
- Impresión física.

El Desktop sí puede usarse como **piloto offline controlado** para los módulos críticos ya validados, pero el Web debe conservarse como referencia y respaldo hasta cerrar los pendientes anteriores.
## Actualizacion de pendientes finales - 2026-06-29

Se trabajo exclusivamente sobre los pendientes finales detectados y no se elimino ni retiro ningun archivo Web.

### Registro App movil completo

Referencia Web localizada:

- `Controllers/PosController.cs`: `RegistroApp`, `BuscarRegistroAppTaxistas`, `BuscarRegistroAppTarifas`, `BuscarRegistroAppHoteles`, `GuardarRegistroApp`.
- `Services/PosSqlMirrorService.AppMovilSync.cs`: `SearchRegistroAppTaxistasAsync`, `SearchRegistroAppTarifasAsync`, `SearchRegistroAppHotelesAsync`, `CreateRegistroAppAsync`, `EnsureAppMovilMirrorSchemaAsync`, `UpsertAppMovilRegistroAsync`, `EnsureAppMovilFolioControlAsync`, `ReplaceAppMovilRegistroGafetesAsync`.
- `Services/PosSqlMirrorService.Relaciones.cs`: reporte de dejadas, payout/pago dejada y relacion con `AppMovilRegistro`, `AppMovilRegistroGafetes`, `RelacionTicketTaxista`.

Tablas/endpoints identificados:

| Elemento | Uso Web | Estado Desktop offline |
|---|---|---|
| `mkt.AppMovilRegistro` | Registro de viajes de app, folio app/control, taxista, hotel, unidad, importes, payout/dejada. | Importado y usado por reportes/ticket offline cuando existe. |
| `mkt.AppMovilRegistroGafetes` | Gafetes asociados a registro app. | Importado; usado en validaciones/gafetes. |
| `mkt.RelacionTicketTaxista` | Liga folio app, operacion POS, ticket y taxista. | Importado y consultado en Desktop. |
| `mkt.dejadas` | Fuente principal de dejadas/ticket. | Importado y consultado en Desktop. |
| `AppTaxiApiClient` / Hostinger | Busqueda y sincronizacion online de taxistas, tarifas, hoteles y viajes. | No aplica en Desktop 100% offline; se reemplaza por datos importados a SQLite. |

Conclusion: lo que depende de comunicacion movil/API remota queda documentado como no aplicable offline o pendiente de una futura integracion local de captura/sincronizacion. El Desktop no realiza llamadas a Hostinger ni API remota.

### Dejadas/ticket

Referencia Web:

- `Controllers/PosController.cs`: `BuildDejadaReceiptContent`, `BuildDejadaReceiptTicket`, `ReimprimirTicketDejada`, `DescargarTicketDejada`.
- `Views/Pos/Relaciones/DejadaTicket.cshtml`.

Implementado en Desktop:

- Consulta local SQLite sobre `mkt__dbo__dejadas`, `mkt__dbo__AppMovilRegistro` y `mkt__dbo__RelacionTicketTaxista`.
- Generacion de ticket texto con el mismo encabezado, ancho base de 42 columnas y campos del Web: ticket, folio, folio app, operacion, fechas, taxista, vendedor, gafete, unidad, placas, telefono, nacionalidad, transporte, hotel, destino, pax, importe, usuario, estatus y firma del taxista.
- Vista/exportacion local a `.txt`.
- Flujo de impresion fisica desde WPF mediante `PrintDialog`.

Estado: implementado offline; requiere prueba manual con impresora real.

### Cuadre final Excel

Referencia Web:

- `ExportReporteCuadreFinalExcel`.
- `BuildReporteCuadreFinalExcel`.
- `BuildReporteDejadasWorksheet`.
- `BuildReporteHotelesWorksheet`.
- `BuildReporteComisionesResumenWorksheet`.
- `BuildReporteCorteFinalWorksheet`.
- `GetCamionesResumenAsync`.

Implementado en Desktop:

- Boton `Cuadre final Excel`.
- Exportacion `.xlsx` OpenXML multi-hoja offline con `CUADRE`, `CUADRE dejadas`, `REPORTE HOTELES`, `comisiones` y `CORTE FINAL`.
- Nombre de archivo alineado al Web: `cuadre_final_yyyyMMdd_yyyyMMdd.xlsx`.
- Datos desde SQLite local/importada.

Estado: implementado funcionalmente; requiere comparacion visual manual contra Excel Web real para ajustes de color, anchos y estilos finos.

### Guias

Fuente revisada:

- `Data/PosTritonSchema.sql` define `dbo.Guias`.
- `Data/PosEntities.cs` contiene `PosDeptoGuia`.
- Servicios de catalogos exponen el modulo.
- SQLite importada contiene `mkt__dbo__deptoguia`, pero no aporto datos utiles para poblar `LocalGuias`.

Estado Desktop:

- Tabla `LocalGuias` existe.
- Pantalla WPF de alta/edicion/consulta existe.
- Normalizador intenta importar desde `mkt__dbo__deptoguia`.
- Resultado actual: sin datos utiles reales importados.

Conclusion: parcial por falta de fuente real util en la base importada, no por dependencia Web.

### Impresion fisica

Implementado:

- Flujo WPF con `PrintDialog`.
- Ticket de dejada imprimible en fuente monoespaciada.
- Reporte generico imprimible ya existente.

Estado: requiere prueba manual en impresora real, porque el entorno automatico no confirma corte de papel, margenes, driver ni tamano fisico.
