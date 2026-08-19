# Matriz detallada de paridad Web → Desktop

Estado de corte: 26/06/2026. La fuente de verdad es el proyecto Web. Ninguna fila
puede pasar a **validado** hasta comparar resultados con datos reales importados.

| Módulo / función Web | Referencia Web | Lógica / SQL y tablas | Equivalente WPF | Falta y datos reales requeridos | Prueba de paridad | Estado |
|---|---|---|---|---|---|---|
| Ventas: consultar y buscar productos | `PosController.Ventas` (línea 322), `Views/Pos/Ventas/Index.cshtml`, `TryGetVentasAsync` | `Servicios/PosSqlMirrorService.Ventas*.cs`; catálogo tienda `Productos`, cabeceras `remisioM` | No | Portar búsqueda, carrito, impuestos, moneda, productos y remisión. Copia de `mkt`, `compuadmo`, `joyeria`. | Misma búsqueda → mismas líneas, precios, IVA y total. | Pendiente |
| Ventas: agregar/borrar línea | `AgregarLineaVenta`, `BorrarLineaVenta`; vista Ventas | Lógica de líneas en `Ventas.cs` / `VentasHelpers.cs`; tablas de detalle de remisión y productos | No | Reglas de cantidad, stock, precio y eliminación. Datos de remisiones/productos. | Comparar carrito y total por escenarios existentes. | Pendiente |
| Ventas: guardar/cobrar | `GuardarVenta` (999), `VentasCobro.cs` | Inserta cabeceras/detalle, pagos y auditoría; `remisioM`, detalle, `pagosM`, `Pagos`, `AuditoriaMovimiento` | No | Transacción SQLite equivalente y folios. Datos de una venta real completa. | Guardar misma venta en copia y contrastar folio, totales y pagos. | Pendiente |
| Pagos: consulta | `Pagos`, `Views/Pos/Pagos/Index.cshtml`, `TryGetPagosAsync` | `Pagos.cs`; operaciones, comisiones/pagos y tablas `Pagos`, `mov_operacion` | No | Consulta/estado/saldo y filtros. Datos de operaciones con pagos parciales. | Comparar saldo, filas y estado por folio. | Pendiente |
| Pagos: registrar | `GuardarPago` (1108), `RegisterPagoAsync` | Actualiza pago/comisión y audita; `Pagos`, `mov_operacion`, `AuditoriaMovimiento` | No | Validaciones de importe, pago previo y no duplicación. | Pago parcial y total, incluyendo intento inválido. | Pendiente |
| Comisiones: consultar | `Comisiones` (575), `Views/Pos/Comisiones/Index.cshtml` | `Comisiones.cs`: combina `mov_operacion`, `operacion`, `transporte`, `dejadas`, `RelacionTicketTaxista`, `AppMovilRegistro`, `remisioM`, `pagosM`, `egresos/gastos` | No | Portar consultas y tablas cruzadas a SQLite. Se requieren las cuatro bases y registros móviles. | Comparar cada columna, total y paginación en un rango cerrado. | Pendiente |
| Comisiones: recalcular | `CalcularComisiones` (1297), `RecalcularComisionesAsync` | Fórmulas descritas en `Comisiones.cs`; actualiza operaciones y audita | No | Fórmulas, redondeo y transacción. Datos con diferentes transportes/monedas. | Ejecutar antes/después y comparar filas afectadas/importes. | Pendiente |
| Comisiones: abonar/pagar | `PagarComision`, `AbonarComision`, `PagarComisionesSeleccionadas` | `PayComisionAsync`, pagos y auditoría | No | Reglas de pago parcial/masivo y bloqueo de duplicados. | Casos parcial, completo, repetido y selección múltiple. | Pendiente |
| Cortes: calcular | `Cortes` (563), `CalcularCorte` (1289), `TryGetCorteAsync` | `Cortes.cs`; consolida efectivo/tarjeta, pagos y gastos; `Cortes`, `mov_operacion`, `Pagos` | No; solo reporte simple de registros | Fórmula real de diferencia, estado y fecha. Datos de día cerrado y abierto. | Mismo día → mismos totales y diferencia. | Pendiente |
| Cortes: cerrar | `CerrarCorte` (1372), `CloseCorteAsync` | Inserta/actualiza `Cortes`, bloquea y registra auditoría | No | Bloqueo idempotente, usuario y fecha de cierre. | Cerrar dos veces y verificar resultado/auditoría. | Pendiente |
| Reporte operaciones/registro | `Reportes` (646), `ExportRegistroCsv/Excel/Pdf` | `TryGetRegistroDiarioAsync`, `BuildExcel`, `BuildSimplePdf`; `operacion`, `mov_operacion` | Parcial: CSV local y resumen impreso | Mismo layout, columnas, filtros y PDF. Datos de registros reales. | Comparar filas y archivos de referencia. | En proceso |
| Reporte taxis | `ExportReporteTaxiExcel` (1411) | Reporte de taxi/relaciones y tablas POS | No | Plantilla Excel, consulta y filtros. | Comparar libro/hoja/celdas contra Web. | Pendiente |
| Reporte dejadas | `ExportReporteDejadasExcel` (1424) | `GetRelacionesReporteDejadasAsync`; `dejadas`, relaciones, registros móviles | No | Cálculos de pagos/estatus y plantilla. | Mismo rango → misma cantidad e importes. | Pendiente |
| Concentrado/cuadre final | `ExportReporteConcentradoExcel`, `ExportReporteCuadreFinalExcel` | Métodos de exportación del controlador y servicios de comisiones/relaciones | No | Fórmulas y formatos Excel. | Comparar valores y fórmulas/celdas. | Pendiente |
| Pagos/comisiones Excel | `ExportReportePagosComisionesExcel` | `GetPagosComisionesReporteAsync` | No | Consulta, columnas y plantilla. | Comparar lista y totales. | Pendiente |
| PDFs pagos/comisiones/corte | `ExportPagosPdf`, `ExportComisionesPdf`, `ExportCortePdf`, `BuildSimplePdf` | Construcción PDF textual en `PosController` | No | Replicar contenido, anchos, títulos y filtros. | Comparar texto extraído/impresión. | Pendiente |
| Usuarios/permisos | `Usuarios`, `GuardarUsuario` (781), `Views/Pos/Usuarios/Index.cshtml` | `Auth.cs`; `Usuarios`, `UsuarioPermisos`, PBKDF2, auditoría | Parcial: login/filtrado de menú | Formulario visual CRUD, contraseña, roles, todos los permisos, auditoría. Datos de usuarios reales. | Crear/editar usuario y comparar permisos/login. | En proceso |
| Auditoría | `PosSqlMirrorService.Auditoria.cs` | Inserta en `AuditoriaMovimiento`: usuario, módulo, acción, folios, importe, equipo, JSON | No | Tabla SQLite homóloga y llamada transaccional por operación. | Ejecutar cada acción y contrastar registro de auditoría. | Pendiente |
| Gafetes | `Gafetes`, `RegistrarRegresoGafete`, `Gafetes.cs` | `gafete`, `AppMovilRegistroGafetes`, relación y auditoría | Parcial: alta/asignación/devolución simple | Movimientos masivos, folio, relación, bloqueo y auditoría. | Asignar/devolver mismo conjunto y comparar estatus. | En proceso |

## Orden de implementación acordado

1. Ventas y sus transacciones SQLite.
2. Pagos y comisiones, incluidos cálculos comparables.
3. Cortes y bloqueo de cierre.
4. Reportes especializados y exportadores Excel/PDF.
5. Usuarios/permisos y auditoría transaccional.

Los nombres `PosTable` y `AppTable` en los servicios Web se resuelven con los
esquemas configurados; la migración debe normalizar esas tablas en SQLite sin
perder sus claves ni tipos antes de portar cualquier consulta.
