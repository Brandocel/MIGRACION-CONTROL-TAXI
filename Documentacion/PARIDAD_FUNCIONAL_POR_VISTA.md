# Paridad funcional por vista Web vs Desktop

Fecha: 2026-06-30.

Fuente Web revisada:

- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main`

Regla aplicada: no se marca una vista como completa si falta algún botón, acción, filtro, columna, impresión o exportación del Web.

## Resumen por vista

| Vista Web | Pantalla Desktop equivalente | Funcionalidades Web encontradas | Ya existe en Desktop | Agregado en esta integración | Pendiente |
|---|---|---|---|---|---|
| `Views/Pos/Auth/Login.cshtml` | `MainWindow` login | Usuario, contraseña, entrar al POS, aviso de error, branding HOKA. | Login local, branding HOKA, modal visual. | Sin cambio en ZIP. | Revisión pixel-perfect fina. |
| `Views/Pos/Dashboard/Index.cshtml` | `MainWindow` dashboard | Registro Diario, Registro App, Comisiones, Transporte, Guías, Taxistas, Gafetes, Relación Ticket-Taxista, Gastos, Cortes, Reportes, Usuarios, Salir. | Dashboard WPF con tarjetas. | Se retiraron `Portal` y `Checklist` del menú principal por no existir en Web. | Iconos SVG exactos. |
| `Views/Pos/Registro/Index.cshtml` | `OperationsWindow` Registros | Buscar, cierre, comisiones, menú, salir, reportería, guardar, CSV, PDF; tabla operaciones; filtros fecha/taxista/folio. | Alta/consulta registros, guardar, CSV/impresión básica, tabla. | Sidebar visual tipo Web. | Filtros exactos, columnas explícitas y PDF exacto. |
| `Views/Pos/RegistroApp/Index.cshtml` | `OperationsWindow` Registros/App local | Guardar, Registro, Relación, Menú, Salir; datos taxista, gafetes, hotel, transporte, fecha/hora. | Captura local de registros/catálogos. | Regla Web actualizada: ya no se bloquea MAJESTIC monto 0/1. | Flujo visual completo por secciones App. |
| `Views/Pos/Ventas/Index.cshtml` | `PosWindow` Ventas | Borrar línea, Nuevo, Cobrar, Consulta precios, Corte, Gastos, Tickets, Menú, Salir; ticket actual; resultados consulta; remisión. | Productos, ventas, cobrar, Excel/PDF, tablas. | Sidebar visual tipo Web. | Borrar línea, nuevo, consulta precios y remisión detalle exacta. |
| `Views/Pos/Ventas/RemisionDetalle.cshtml` | `PosWindow` reportes/ticket | Volver venta, ver productos, relación, comisión, registro, menú; importes y remisiones relacionadas. | Datos/exportes parciales. | Sin cambio en ZIP. | Pantalla WPF específica de remisión detalle. |
| `Views/Pos/Ventas/RemisionProductos.cshtml` | `PosWindow` ventas/productos | Tabla productos de remisión, volver. | Productos en DataGrid. | Sin cambio en ZIP. | Vista exacta de productos por remisión. |
| `Views/Pos/Pagos/Index.cshtml` | `PosWindow` Pagos | Registrar pago, CSV/PDF, filtros y tabla pagos. | Registrar pago, CSV, PDF, tabla. | Sidebar visual tipo Web. | Columnas/filtros exactos por Web. |
| `Views/Pos/Comisiones/Index.cshtml` | `PosWindow` Comisiones | Recalcular, pagar/abonar, CSV/PDF, estado comisión, pagos. | Recalcular, abonar, CSV/PDF, tabla. | Reglas Web actualizadas de Majestic, Salmoran/Turibus/ADO, tarjeta/AMEX y deducción dejada mayor. | Comparación folio por folio tras datos reales importados. |
| `Views/Pos/Cortes/Index.cshtml` | `PosWindow` Cortes | Calcular, cierre, CSV/PDF, tabla cortes. | Calcular, cerrar, CSV/PDF, tabla. | Sidebar visual tipo Web. | Formato visual/impresión exacta. |
| `Views/Pos/Gafetes/Index.cshtml` | `OperationsWindow` Gafetes | Guardar, asignar, regreso, regreso en bloque, CSV, imprimir, menú. | Guardar, asignar, devolver, imprimir básico. | Sidebar visual tipo Web. | Regreso en bloque exacto y selección por checkbox. |
| `Views/Pos/Gastos/Index.cshtml` | `OperationsWindow` Gastos | Guardar gasto, menú/salir, tabla. | Guardar gasto, tabla. | Sidebar visual tipo Web. | Edición/eliminación si aplica en Web. |
| `Views/Pos/Relaciones/Index.cshtml` | `OperationsWindow` Relaciones | Actualizar, limpiar/nuevo, comisión, menú, salir; pagar comisión/dejada; imprimir ticket. | Guardar relación, tabla, ticket desde reportes. | Campo `SeFueron` retirado del Desktop por actualización Web. | Acciones por fila exactas: venta, pagar, calcular, imprimir. |
| `Views/Pos/Relaciones/DejadaTicket.cshtml` | `TicketPreviewWindow` | Ticket imprimible. | Vista previa, guardar TXT, imprimir. | Ventana visual de ticket. | Formato físico exacto depende de impresora. |
| `Views/Pos/Reportes/Index.cshtml` | `PosWindow` Reportes | Taxi, Dejadas, Concentrado, Pagos/comisiones, Gafetes, Cuadre final, Excel/CSV/PDF según flujo. | Exportaciones principales. | Textos `Salieron` conservados en modelos locales. | Formato Excel exacto por plantilla Web. |
| `Views/Pos/Usuarios/Index.cshtml` | `UserAdminWindow` | Usuarios, roles, permisos, guardar. | Usuarios/permisos locales. | Modal visual Web para errores. | Distribución visual exacta. |
| `Views/Pos/Catalogos/Index.cshtml` | `OperationsWindow` Catálogos | Transportes, guías, taxistas, hoteles, tarifas. | Catálogos locales. | Sidebars visuales por módulo. | Vista agrupada exacta como Web. |
| `Views/Portal/*.cshtml` | `PortalWindow` | Dashboard, operaciones, comisiones, vendors, products, catalogs, create operation. | PortalWindow existe como pantalla interna, no en dashboard POS. | Sin menú principal porque no existe en Dashboard POS Web. | Paridad visual/funcional Portal por pestaña. |

## Evidencia de que no se modificó lógica validada de forma destructiva

- No se alteró la estructura física de `DatosLocal/ControlTaxi.db`.
- No se eliminó la columna `SeFueron`; solo se dejó de usar en UI/modelo Desktop.
- No se modificó SQL Server.
- No se eliminó el Web.
- No se agregaron menús principales que no existan en `Views/Pos/Dashboard/Index.cshtml`.

## Estado

Las actualizaciones detectadas en el ZIP fueron integradas donde correspondía al Desktop. La paridad funcional total por vista todavía requiere cerrar pendientes marcados en la tabla, especialmente acciones por fila, filtros/columnas explícitas y formatos Excel/PDF exactos.
