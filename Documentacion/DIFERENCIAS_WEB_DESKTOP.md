# Diferencias Web vs Desktop

Fecha: 26/06/2026.

Este reporte compara el sistema Web conservado como referencia contra la
implementación WPF local actual. No se elimina ni retira ningún archivo Web.

## Actualización con datos reales importados

Fecha: 26/06/2026 15:30.

Ya existe `DatosLocal/ControlTaxi.db` con importación real desde SQL Server local:

- `mkt2` importada como `mkt`.
- `compuadmoPlaza` importada como `compuadmo`.
- `joyeriaPlaza` importada como `joyeria`.
- `ControlTaxis` importada como `ControlTaxis`.

Se normalizaron datos reales hacia tablas Desktop:

| Tabla Desktop | Filas |
|---|---:|
| `LocalProductos` | 33,945 |
| `LocalVentas` | 4,499 |
| `LocalVentaLineas` | 22,041 |
| `LocalPagos` | 5,414 |
| `LocalComisiones` | 4,499 |
| `LocalCortes` | 684 |
| `LocalGafetes` | 555 |
| `LocalAuditoria` | 2,553 |
| `DesktopUsers` | 2 |
| `DesktopPermissions` | 26 |

## Corrección aplicada a diferencias reales

Fecha: 26/06/2026 15:40.

Se corrigió el normalizador Desktop para tratar los folios repetidos de
`mkt.mov_operacion` igual que el origen real: una venta local por folio, pero
sumando todos los movimientos asociados a ese folio.

| Módulo | Resultado corregido | Estado |
|---|---|---|
| Ventas | Origen: 9,843 movimientos / 4,499 folios. Desktop: 4,499 ventas consolidadas por folio. Total origen/Desktop: 34,865,759.84. | Validado con datos reales importados |
| Pagos | Total origen efectivo+tarjeta: 25,912,396.39. Total Desktop: 25,912,396.39. | Validado con datos reales importados |
| Comisiones | Suma origen `mkt.mov_operacion.comision`: 517,109.15. Suma Desktop: 517,109.15. Folios diferentes: 0. | Validado con datos reales importados |

El detalle folio por folio quedó en `DIFERENCIAS_FOLIOS_PAGOS_COMISIONES.md`.

### Diferencias reales detectadas

| Módulo | Resultado | Estado |
|---|---|---|
| Ventas | `mkt.mov_operacion` trae 9,843 filas, pero 4,499 folios distintos. Desktop creó 4,499 ventas por folio único. El total origen es 34,865,759.84 y el total Desktop normalizado es 16,483,960.74. | Pendiente por diferencia |
| Pagos | Total origen efectivo+tarjeta: 25,912,396.39. Total Desktop: 14,571,957.64. | Pendiente por diferencia |
| Comisiones | Suma origen `mkt.mov_operacion.comision`: 517,109.15. Suma Desktop calculada: 385,020.00. Folios diferentes: 2,280. | Pendiente por diferencia |
| Cortes | 684 días origen y 684 cortes Desktop. Esperado origen/Desktop: 34,865,759.84. | Validado con datos reales importados para agregados diarios base |
| Gafetes | 555 gafetes distintos origen y 555 gafetes Desktop. | Validado con datos reales importados |
| Auditoría | 2,553 registros origen y 2,553 Desktop. | Validado con datos reales importados |
| Usuarios/permisos | 2 usuarios y 26 permisos origen/Desktop. | Validado con datos reales importados |
| Excel/PDF | Exportan desde tablas locales reales, pero falta comparación visual/formato contra archivos generados por Web. | Pendiente por comparación de formato |

Nota importante: `ControlTaxis.Comisiones`, `ControlTaxis.Cortes`, `ControlTaxis.Ventas` y `ControlTaxis.Pagos` vienen con 0 filas. Por eso comisiones/cortes se comparan contra `mkt.mov_operacion` y no contra un baseline persistido en `ControlTaxis`.

## Estado general

Ya se importaron datos reales en `DatosLocal/ControlTaxi.db`. Algunos módulos
quedaron validados por comparación de conteos/sumas contra las tablas reales
importadas; ventas, pagos y comisiones permanecen pendientes por diferencias
detectadas y documentadas arriba. No se eliminó ni retiró ningún archivo Web.

## Estado por módulo

| Módulo | Estado | Motivo |
|---|---|---|
| Ventas | Implementado | Existe pantalla WPF POS, productos, venta transaccional, líneas, stock, subtotal, IVA y total. Falta validar contra datos reales y completar todos los campos Web. |
| Pagos | Implementado | Existe registro local de pagos, saldo y estado. Falta comparar contra `TryGetPagosAsync` y `RegisterPagoAsync` con folios reales. |
| Comisiones | Implementado | La fórmula Web base para `mov_operacion/transporte` ya fue migrada al Desktop y al normalizador. Falta validar con datos reales y cubrir ramas enriquecidas de App/Dejadas. |
| Cortes | Implementado | El cálculo Web base desde `mov_operacion`, `transporte` y `ControlTaxis.Cortes` ya fue migrado. Falta validar con datos reales. |
| Reportes | Implementado | Existen consultas/exportaciones offline para taxi, dejadas/concentrado base, pagos/comisiones y gafetes. Falta validar con datos reales y completar cuadre final hoja por hoja. |
| Excel/PDF | Implementado | Existe exportación `.xlsx` OpenXML local y PDF básico. Falta comparar layout exacto contra Web con datos reales. |
| Usuarios y permisos | Implementado | Login local, bloqueo por módulo y administración visual de usuarios/permisos existen. Falta validar contra usuarios reales importados. |
| Auditoría | Implementado | `LocalAuditoria` ya conserva campos equivalentes a `AuditoriaMovimiento`. Falta comparar registros contra datos reales. |
| Gafetes | Implementado | Alta/asignación/devolución local e historial importado desde `mkt__dbo__gafete` existen. Falta validar y enriquecer contra `AppMovilRegistro`. |

## Diferencias encontradas

### Ventas

Referencia Web:

- `Services/PosSqlMirrorService.Ventas.cs`
- `Services/PosSqlMirrorService.VentasCobro.cs`
- `Models/PosTriton/PosVentasViewModel.cs`

Campos Web que deben conservarse/validarse:

- `Vendedor`
- `Cliente`
- `Usuario`
- `FolioControl`
- `FolioApp`
- `Gafete`
- `TransporteTipo`
- `GuiaMatricula`
- `TaxistaId`
- `TaxistaNombre`
- `ProductoBusqueda`
- `ProductoId`
- `Cantidad`
- `Pax`
- `Efectivo`
- `Tarjeta`
- `Dolares`
- `TipoCambio`
- `Subtotal`
- `Iva`
- `Total`
- `Items`
- `ResultadosBusqueda`
- `GafetesActivos`
- `GafeteVentas`
- `FolioRegistro`
- `FolioFactura`
- `OrigenVenta`

Diferencia Desktop:

- `LocalVenta` todavía maneja una venta POS simplificada: folio, cliente,
  estatus, subtotal, IVA, total, pagado y estado.
- Falta normalizar ventas importadas desde `mov_operacion`, `operacion`,
  `Productos`, remisiones y relaciones de gafete/taxista.

### Pagos

Referencia Web:

- `Services/PosSqlMirrorService.Pagos.cs`
- `Models/PosTriton/PosPagosViewModel.cs`

El Web calcula:

- `TotalVenta`
- `TotalComision`
- `TotalPagado`
- `Saldo`
- `ImporteCaptura`
- pagos por folio con estatus `APLICADO`, `PENDIENTE`, `SIN COMISION`

Diferencia Desktop:

- El pago local trabaja sobre `LocalVentas`.
- Falta vincularlo con la comisión real de `mov_operacion.Comision/Pago`.

### Comisiones

Referencia Web:

- `Services/PosSqlMirrorService.Comisiones.cs`
- `Models/PosTriton/PosComisionesViewModel.cs`
- `Services/PosSqlMirrorService.Cortes.cs`

El Web usa, entre otros:

- transporte;
- venta joyería/compra/artesanía/farmacia/licor;
- base;
- porcentaje;
- descuentos por tarjeta/efectivo;
- dejadas;
- gastos;
- degustación;
- reparación;
- bebidas;
- cajas regalo;
- pago y fecha de pago.

Diferencia Desktop:

- Cuando existe `mkt__dbo__mov_operacion`, Desktop ya usa la fórmula Web base:
  venta joyería + compra, dejada, bebidas/licor, degustación/gastos,
  descuento efectivo/tarjeta, comisión fija o porcentaje y truncado.
- Cuando no existen datos reales importados, Desktop conserva el flujo local
  temporal para poder probar la pantalla.
- Falta validar contra datos reales y completar ramas enriquecidas de App Móvil,
  dejadas y pagos de control.

### Cortes

Referencia Web:

- `Services/PosSqlMirrorService.Cortes.cs`
- `Models/PosTriton/PosCorteViewModel.cs`

El Web calcula:

- `Efectivo`
- `Tarjeta`
- `Amex`
- `Gastos`
- `Comisiones`
- `TotalDia`
- `Diferencia = TotalDia - (Efectivo + Tarjeta)`
- `Movimientos`
- `Cerrado`

Diferencia Desktop:

- Cuando existe `mkt__dbo__mov_operacion`, Desktop ya calcula efectivo, tarjeta,
  gastos, total del día, comisión y diferencia con la fórmula Web base.
- Cuando no existen datos reales importados, conserva el corte local de prueba.
- Falta validar contra datos reales y reportar movimientos con el mismo detalle
  visual del Web.

### Auditoría

Referencia Web:

- `Services/PosSqlMirrorService.Auditoria.cs`
- tabla `AuditoriaMovimiento`

Campos Web:

- `FechaUtc`
- `Usuario`
- `Modulo`
- `Accion`
- `IdRegistro`
- `Descripcion`
- `BaseDatos`
- `Tabla`
- `FolioApp`
- `FolioOperacion`
- `FolioPos`
- `Taxista`
- `Gafete`
- `Importe`
- `Exito`
- `Equipo`
- `Aplicacion`
- `DetalleJson`

Diferencia Desktop:

- `LocalAuditoria` ya fue ampliada para conservar campos equivalentes:
  `IdRegistro`, `Descripcion`, `BaseDatos`, `Tabla`, `FolioApp`,
  `FolioOperacion`, `FolioPos`, `Taxista`, `Gafete`, `Exito`, `Equipo`,
  `Aplicacion` y `Detalles`.
- Falta validar con datos reales que cada acción genere el mismo tipo de
  auditoría que el Web.

## Correcciones pendientes

1. Normalizar datos reales importados hacia tablas Desktop equivalentes.
2. Validar la fórmula Web de comisiones con datos reales importados.
3. Validar cortes contra datos reales importados.
4. Completar ramas App/Dejadas enriquecidas cuando exista base real con sus vistas/tablas.
5. Comparar exportaciones Excel/PDF contra los métodos del controlador Web.
6. Crear pruebas de comparación por folio/rango usando `DatosLocal/ControlTaxi.db`.
7. Completar cuadre final multihoja con todas las hojas del Web usando datos reales.

## Archivos Web conservados como referencia

- `Controllers/PosController.cs`
- `Services/PosSqlMirrorService.Ventas.cs`
- `Services/PosSqlMirrorService.VentasCobro.cs`
- `Services/PosSqlMirrorService.VentasHelpers.cs`
- `Services/PosSqlMirrorService.Pagos.cs`
- `Services/PosSqlMirrorService.Comisiones.cs`
- `Services/PosSqlMirrorService.Cortes.cs`
- `Services/PosSqlMirrorService.Auditoria.cs`
- `Views/Pos/**/*.cshtml`
- `Models/PosTriton/*.cs`
