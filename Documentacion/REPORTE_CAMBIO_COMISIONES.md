# Reporte de Cambio — Módulo de Comisiones

**Fecha:** 2026-07-14
**Área:** ControlTaxiDesktop → Vista de Comisiones
**Base analizada:** `DatosLocal/ControlTaxi.db` (SQLite, 453 MB) — solo lectura, no se modificó SQL Server ni el sistema Web.
**Estado:** Diagnóstico cerrado con evidencia. Corrección propuesta, pendiente de implementar y validar.

---

## 1. Problema reportado

La Vista de Comisiones muestra una venta y comisión **menores a la venta real**. Hay tickets vendidos que existen pero no se reflejan en el cálculo. Caso testigo: **Antonio Flores**, venta real **$1,010**, el sistema refleja **$581**.

Adicionalmente se pide aplicar correctamente el reparto por tipo de participante en `dbo.vendrem`:
- **V = Vendedor** → participa en el reparto de venta y comisión.
- **P = Pasador** → solo identifica, importe y comisión en **$0**.

---

## 2. Alcance técnico

- **Pantalla afectada:** "Comisiones" (`PosWindow(..., "Comisiones")`).
- **Método que la alimenta:** `LocalPosRepository.GetCommissionBrowserRowsAsync(...)`
  (`ControlTaxiDesktop/Services/LocalPosRepository.cs`).
- Como existen las tablas importadas `mkt__dbo__*`, la ejecución entra por
  `GetCommissionBrowserRowsFromImportedMovOperationsAsync` → `LoadImportedAuthoritativeCommissionRowsAsync`.
- Ese camino enlaza los tickets de tienda por `folio_pos` exacto y **nunca consulta `vendrem`**.

---

## 3. Causas raíz (verificadas contra datos reales)

### 3.1 🔴 Enlace de ticket roto — `folio_factura` no es la factura
En `compuadmo__dbo__remisioM` la columna `folio_factura` está en **`0` en todas las filas**. La venta vive en `remisioM.total` y la llave real es `remisioM.folio_remision`. En cambio, `vendrem.folio_factura` **contiene el folio de remisión** (ej. `BA1712411`).

| Unión probada | Folios que empatan |
|---|---|
| `vendrem.folio_factura = remisioM.folio_factura` | **0** ❌ |
| `vendrem.folio_factura = remisioM.folio_remision` | **204,057 / 204,078 (99.99%)** ✅ |

Cualquier consulta que una por `folio_factura = folio_factura` recupera **cero** venta → los tickets "desaparecen".

### 3.2 🔴 El reparto V/P no está implementado
- `compuadmo__dbo__vendrem`: 306,605 filas → **V = 284,243**, **P = 17,400**.
- **73,753 folios** tienen ≥2 participantes (requieren reparto); 9,557 con V y P juntos.
- El código del Desktop no reparte la venta entre los vendedores V ni pone al pasador en $0.

### 3.3 🟠 Campo `tipo` sin normalizar
Valores reales: `V`=284,243, `P`=17,400, **vacío=4,959**, más `v`/`p` en minúscula. Un filtro `tipo='V'` pierde las minúsculas y no define el trato de los vacíos.

### 3.4 🟠 Cobertura y filtros que ocultan tickets
- El enlace por `folio_pos` exacto + `OUTER APPLY TOP 1` recupera **un solo** ticket por operación.
- Solo `compuadmo` tiene `vendrem`; **joyería no** → sus tickets no reparten.
- Filtros de `estatus` y de fecha pueden excluir ventas válidas si no se aplican con cuidado.

> La combinación de **3.1 + 3.4** es la que deja a Antonio Flores en $581 en vez de $1,010: los tickets multi-vendedor y los que no empatan por `folio_pos` exacto se caen del total.

---

## 4. Naturaleza de los casos reportados

| Caso | Qué es | Ubicación |
|---|---|---|
| Antonio Flores | Taxista / pasador (staff) | `mkt__dbo__dejadas.nombrestaff` (transporte SALMORAN / VAN) |
| Plaza 28 | Tienda / almacén 100 | `compuadmo__dbo__almacenes` |
| Mayan Market | Tienda / almacén 160 | `compuadmo__dbo__almacenes` |
| Casco (CascoViejo) | Tienda / almacén 186 | `compuadmo__dbo__almacenes` |
| Xunán / Diverto | Tiendas | `compuadmo__dbo__almacenes` |

El caso de Antonio Flores es **tickets faltantes** (venta subcontada). El reparto V/P es un requisito **independiente** sobre cómo se divide la comisión entre los vendedores de tienda.

---

## 5. Corrección propuesta

| # | Defecto | Corrección |
|---|---|---|
| 1 | Enlace roto | Unir por `vendrem.folio_factura = remisioM.folio_remision` |
| 2 | Sin reparto V/P | Dividir `remisioM.total` entre los V (**partes iguales**); P y vacíos en $0 |
| 3 | `tipo` sin normalizar | Comparar con `UPPER(TRIM(tipo)) = 'V'` |
| 4 | Tickets ocultos | Recuperar todos los tickets del período por `folioregistro`; excluir solo cancelados (`estatus = 'C'`, `'CANCELADO'`, `'CANCELADA'`) |

### Regla de reparto confirmada (partes iguales)
`venta_vendedor = total_ticket / número_de_V`  ·  el pasador (P) queda en $0.

### Evidencia del reparto (ticket real activo)

| folio_factura | total | vendedor | tipo | venta repartida |
|---|---|---|---|---|
| BA10020318076 | $1,500 | JHONATHAN LEON | V | **$500** |
| BA10020318076 | $1,500 | EDUARDOEMANUEL HERNANDEZ | V | **$500** |
| BA10020318076 | $1,500 | AXEL SALOMON SELADA | V | **$500** |
| BA10020318076 | $1,500 | SHEISLIER ORTEGA | **P** | **$0** |

---

## 6. Estrategia de implementación

- Implementar la lógica corregida como un **método nuevo y aislado**, activado cuando existe `compuadmo__dbo__vendrem`, **sin tocar** el resto del pipeline de comisiones.
- Ventaja: cambio **revisable y reversible**; el flujo actual queda intacto como respaldo.

---

## 7. Validación pendiente (antes de dar por cerrado)

- [ ] Confirmar el **período** del caso Antonio Flores = $1,010 para reproducir $581 → $1,010.
- [ ] Prueba de humo sobre `ControlTaxi.db`: totales nuevos por tienda (Plaza 28, Mayan Market, Casco, Xunán, Diverto) y por taxista.
- [ ] Cotejar la venta total contra el **control del área operativa**.
- [ ] Verificar que la comisión solo recae en vendedores V y que los pasadores P quedan en $0.

---

## 8. Riesgos y notas

- El módulo paga comisiones reales: el cambio se validará contra el control operativo antes de sustituir el flujo actual.
- Joyería no tiene `vendrem`: sus tickets no reparten por vendedor (se tratan como venta única). Confirmar si aplica reparto en joyería.
- No se modifica SQL Server ni el sistema Web; todo el análisis y la corrección operan sobre la base local del Desktop.
