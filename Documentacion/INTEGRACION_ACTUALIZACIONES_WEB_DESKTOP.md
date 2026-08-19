# Integración de actualizaciones Web hacia Desktop

Fecha: 2026-06-30.

ZIP revisado:

- `C:/Users/reyna/Downloads/PosSqlMirrorService.AppMovilSync.cs-main (1).zip`

Carpeta de referencia extraída:

- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main`

## Archivos del ZIP revisados

Se revisaron:

- `Controllers/PortalController.cs`
- `Controllers/PosController.cs`
- `Views/Portal/*.cshtml`
- `Views/Pos/**/*.cshtml`
- `Services/*.cs`
- `Models/PosTriton/*.cs`
- `Models/PortalViewModels.cs`
- `Data/*.cs`
- `Interfaces/*.cs`

## Cambios reales detectados contra la referencia Web anterior

| Estado | Archivo |
|---|---|
| Modificado | `Controllers/PosController.cs` |
| Modificado | `Data/PosEntities.cs` |
| Modificado | `Models/PosTriton/PosRelacionesViewModel.cs` |
| Modificado | `Services/PosSqlMirrorService.Comisiones.cs` |
| Modificado | `Services/PosSqlMirrorService.Relaciones.cs` |
| Modificado | `Services/PosSqlMirrorService.Schema.cs` |

No se detectaron cambios por hash en:

- `Views/Portal/*.cshtml`
- `Views/Pos/**/*.cshtml`
- `Controllers/PortalController.cs`

## Cambios Web detectados

1. `SeFueron` fue retirado del flujo Web de Relaciones:
   - eliminado de `PosRelacionesViewModel`;
   - eliminado de `PosEntities`;
   - eliminado de `PosSqlMirrorService.Schema`;
   - eliminado de consultas/guardado de `RelacionTicketTaxista`.

2. Registro App móvil:
   - se eliminó el bloqueo que impedía guardar operaciones MAJESTIC/MAESTIC con monto `0` o `1`.

3. Reportes Excel/cuadre:
   - textos cambiados de `SE FUERON` a `SALIERON`;
   - reporte de dejadas deja de exportar columna extra `SE FUERON INFO`;
   - el folio del reporte prioriza `FolioApp`, `IdStaff`, `FolioControl`, `FolioOperacion`, `FolioPos`, `TicketPagoDejada`.

4. Comisiones:
   - Majestic/Maestic usa regla 8%;
   - Salmoran/Turibus/ADO usa regla 20%;
   - tarjeta usa descuento bancario 19%;
   - AMEX usa 21%;
   - efectivo no descuenta;
   - se aplica regla de dejada a la venta mayor del grupo;
   - se normalizan tickets de pago quitando espacios y aceptando múltiples candidatos separados por `/`, `|`, `,`, `;`;
   - reglas de deducción se concentran en `CalculateExcelRuleDeductions`.

5. Relaciones/pagos:
   - se corrige consulta contra bases de pago usando nombre real de base de tienda;
   - moneda en joyería usa `m.moneda`.

## Cambios integrados al Desktop

| Cambio Web | Integración Desktop |
|---|---|
| Quitar `SeFueron` del flujo Relaciones | `LocalRelation` ya no expone `LeftCount`; `OperationsWindow` ya no muestra el campo; `LocalOperationsRepository` ya no lee ni guarda `SeFueron`. |
| Mantener compatibilidad de SQLite | No se eliminó físicamente la columna `SeFueron` de `LocalRelaciones`; se conserva para no romper bases/importadores existentes. |
| Menú principal real del Web no tiene Portal/Checklist | Se retiraron los botones `PORTAL` y `CHECKLIST` del dashboard principal Desktop. |
| Reglas nuevas de comisiones | `LocalPosRepository.CalculateWebCommission` actualizó Majestic 8%, Salmoran/Turibus/ADO 20%, tarjeta 19%, AMEX 21%, efectivo 0%, deducciones tipo Excel y regla de dejada por venta mayor. |
| Vista previa/impresión ticket | Se conserva `TicketPreviewWindow` y `PrintText`, funcionando offline. |

## Vistas comparadas

| Vista Web | Cambio en ZIP | Equivalente Desktop | Estado |
|---|---|---|---|
| `Views/Pos/Dashboard/Index.cshtml` | Sin cambio de hash | `MainWindow` | Menú ajustado: sin `Portal` ni `Checklist`. |
| `Views/Pos/Relaciones/Index.cshtml` | Sin cambio de hash, modelo/servicio cambió | `OperationsWindow` / Relaciones | Campo `SeFueron` retirado del Desktop. |
| `Views/Pos/RegistroApp/Index.cshtml` | Sin cambio de hash, controlador cambió | `OperationsWindow` / Registros | Desktop no bloquea MAJESTIC monto 0/1. |
| `Views/Pos/Comisiones/Index.cshtml` | Sin cambio de hash, servicio cambió | `PosWindow` / Comisiones | Reglas de cálculo actualizadas. |
| `Views/Pos/Reportes/Index.cshtml` | Sin cambio de hash, exportación cambió | `PosWindow` / Reportes | `Salieron` ya existe en modelos locales; pendiente formato Excel exacto por columnas Web. |

## Menús eliminados o ajustados por no existir en el Web

Se retiraron del menú principal Desktop:

- `PORTAL`
- `CHECKLIST`

No se eliminó código fuente ni pantallas; solo se retiraron del dashboard principal para respetar la vista principal real del Web.

## Diferencias pendientes

- Las vistas `.cshtml` no cambiaron en el ZIP, pero la paridad visual/funcional pixel-perfect por vista sigue dependiendo de comparar controles WPF con cada vista Web.
- El formato Excel exacto del Web para reportes avanzados requiere mapeo de columnas explícitas; Desktop aún usa exportadores genéricos para varias tablas.
- La columna SQLite `SeFueron` se conserva por compatibilidad, aunque ya no se usa en Desktop.

## Confirmaciones

- No se modificó SQL Server.
- No se borró el Web.
- No se usó internet, WebView, navegador, localhost ni servidor.
- No se modificó la base SQLite de forma destructiva.
- No se agregaron menús que no existan en el Dashboard Web.
- La lógica validada no fue reemplazada; solo se integraron reglas detectadas en el Web actualizado.
- Desktop sigue funcionando offline.
