# Contexto de trabajo — 19 de septiembre de 2026

Reglas de comisión de **Casco Viejo** a partir del Excel que mandó el negocio:
`CALCULO DE COMISIONES, PTO MORELOS.xlsx` (hojas MATILDE, CASCO, DEJADA, DEGUSTACION).
Se usó la hoja **CASCO**.

Continuación de `CONTEXTO-SESION-2026-09-18.md`.

---

## 1. Lo que había antes

- El motor de Casco (`CascoCommissionRuleService` + `CascoCommissionCalculator`) ya se había
  construido en julio con **este mismo Excel**: reconoce las unidades y trae la regla especial de
  Aventuras Mayas. Los porcentajes viven en `dbo.ControlTaxiComisiones` del SQL de Casco.
- **Todas las reglas estaban apagadas** (`Activo = 0`, `RequiereValidacion = 1`). Sin reglas activas,
  Casco calculaba **10 % parejo de la venta**, sin quitar banco, dejada, gasto ni degustación
  (`OrigenComision = "Respaldo"`).
- **La carga de julio había leído mal el Excel**: puso la retención del banco (19 %) como
  `ComisionAgencia`, dejó la "deportiva" como pesos fijos y Aventuras Mayas con
  `VentaMinima = 1000` (sin regla para ventas menores). Por eso nunca se activó.

## 2. Decisiones del negocio (19/09/2026)

| Tema | Decisión |
|---|---|
| Comisión de vendedor ("deportiva") y meta diaria | **Fuera.** No se calcula. |
| Tabulador de degustación, farmacias por producto, 20 % al vendedor que pasa al cliente | **No se construyen.** |
| EXTREME | 10 % al **guía**. |
| MAJESTIC | 8 % guía + 4 % agencia, y el 19 % se quita **también sin tarjeta** (así dice el Excel). |
| FARMACIAS | **Pendiente.** Sin regla. |
| AVENTURAS MAYAS | Los $100 por cada $1,000 aplican desde **$1,000 exactos** (el código ya lo hacía con `>=`). |

Efecto conocido de la regla de Aventuras Mayas: una venta de $999 deja más comisión ($91.10) que
una de $1,000 ($79.20), porque en $1,000 entra el descuento de $100.

## 3. Reglas cargadas (14)

Fórmula: **venta − 19 % banco − dejada − gasto − degustación = base**, y a la base el porcentaje.
Cada regla dice qué descuentos le tocan.

| Unidad | Taxi / guía | Agencia | 19 % banco | Dejada |
|---|---|---|---|---|
| BIKE CID | — | 10 % | sólo tarjeta | sí |
| TAXIS/VANS | 10 % | — | sólo tarjeta | sí |
| CALLE | — | — | sólo tarjeta | no | *(comisión $0: en el Excel sólo llevaba vendedor)* |
| EXTREME | 10 % | — | sólo tarjeta | no |
| AVENTURAS MAYAS | 10 % | 2 % | sólo tarjeta | no |
| MAJESTIC | 8 % | 4 % | **siempre** | no |
| VENTAS ENTRE TIENDAS | — | 50 % (com. Matilde) | sólo tarjeta | no |

Gasto y degustación se descuentan en todas. FARMACIAS sin regla.

## 4. Cambios en el programa

`Services/CascoCommissionRuleService.cs`:

- `CascoCommissionRule` trae cuatro banderas: `AplicaRetencion`, `AplicaDejada`, `AplicaGasto`,
  `AplicaDegustacion`. Antes el cálculo aplicaba el 19 % y los tres descuentos a **todas** las
  reglas, aunque el pago fuera en efectivo.
- `LoadActiveRulesAsync` crea las columnas si faltan (comando aparte del SELECT: SQL Server compila
  el lote completo y no deja usar en el mismo lote una columna recién agregada). `NULL` = el
  comportamiento de antes (`AplicaRetencion` toma el valor de `ConTarjeta`; las demás, 1).
- `ResolveCommissionAmount`: **sin regla → 10 % de respaldo, igual que antes**, en lugar de $0.
  Así FARMACIAS (pendiente) no se va a cero en cuanto se activan las demás. El preview que se
  guarda (`PreviewAsync`) hace lo mismo.

`Services/CascoOperationsDataService.cs`: la vista previa ahora le pasa la **dejada** del renglón
(`row.Payout`). Antes no le pasaba nada y TAXIS/VANS salía sin descontar la dejada. Gasto y
degustación siguen en cero ahí porque se capturan al generar la comisión.

**Verificado con el motor compilado** (programa de prueba que llama a los métodos reales): 17
casos — las 7 unidades con tarjeta y efectivo, Farmacias en respaldo, y Aventuras Mayas en $999
y $1,000 — todos coinciden con el cálculo a mano del Excel.

## 5. El script de carga

`SyncTaxi/aplicar-reglas-comisiones-casco.ps1` + `APLICAR-REGLAS-COMISIONES-CASCO.cmd` (viajan
en el paquete). Sólo corre donde está `casco.credentials.dat`.

1. Muestra las reglas de Casco que hay y pide escribir **SI**.
2. Respalda la tabla en `dbo.ControlTaxiComisiones_Respaldo_20260919`.
3. Apaga las activas y carga las 14 (`OrigenExcel = 'CASCO 2026-09-19'`, nombre con sufijo
   `(19/09/2026)`).
4. `-Revertir` las apaga y restaura el `Activo` de cada regla desde el respaldo. Volver a aplicar
   después de revertir las reactiva sin duplicarlas.

**Ojo:** la tabla tiene el índice único `UX_ControlTaxiComisiones_Regla` sobre
(BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, **ReglaNombre**). Las reglas de julio
ya usan "BIKE CID C/TARJETA", etc.; por eso las nuevas llevan el sufijo de fecha.

**Ensayado completo** en una base temporal local con la tabla creada por los scripts de julio
(`CASCO_CONTROL_TAXI_COMISIONES_PRODUCCION.sql` + `..._SEED_PRODUCCION.sql`): aplicar → 14 activas,
la consulta del escritorio las lee, revertir → 0, volver a aplicar → 14 sin duplicar. El primer
ensayo encontró el choque con el índice único, que habría fallado en producción.

## 6. Paquete

`PLAZA28-DESKTOP-20260919.zip` (73.3 MB, 477 archivos). Mismo paquete para Plaza 28 y Casco; el
cambio sólo afecta el cálculo de Casco.

**Orden en Casco: primero el paquete, después el script de reglas.** Con el programa anterior las
banderas no existen: el 19 % se quitaría también en efectivo.

## 7. Pendientes

1. Instalar `PLAZA28-DESKTOP-20260919.zip` en Casco y correr `APLICAR-REGLAS-COMISIONES-CASCO.cmd`.
2. FARMACIAS: definir cómo se distingue antibiótico (10 %) de controlado (20 %).
3. Hoja **MATILDE** del Excel: no se ha confirmado si aplica a Casco.
4. Lo que quedó fuera por decisión: vendedor/deportiva, meta diaria, tabulador de degustación,
   20 % al vendedor que pasa al cliente.

---

## 8. Plaza 28: MAJESTIC con $500 fijos por llegada (19/09/2026)

Regla nueva del negocio, **sólo Plaza 28** (Casco usa su propio motor y no cambia):
venta − retención (tarjeta 19 % / efectivo 10 %) − gastos − **$500 por llegada** = base, × 8 %.

- Se descuenta **una vez por llegada**, no por ticket: 3 tickets de $500 en la misma llegada
  descuentan $500 en total (se cargan al ticket mayor, como la dejada).
- **Sin umbral de venta chica** y sin tope en cero: venta de $150 en efectivo da base −$365 y
  comisión −$29. El negativo se cobra contra los otros folios, como ya pasaba con la dejada.
- Manda el catálogo, no lo capturado: aunque la operación traiga otra dejada, van $500.
- TRAVEL EXPERIENCE no lleva el descuento.
- **Aplica a todo el histórico** (el usuario lo pidió así después de instalar, para ver cómo se
  aplica en folios viejos). MAJESTIC es un solo periodo desde `Inicio` con
  `Extra: new(DescuentoFijoPorLlegada: 500m, UmbralVentaChica: 0m)`. Consecuencia: folios de
  Majestic ya pagados pueden salir con saldo a favor del negocio. Si se decide una fecha de
  arranque, se parte en dos periodos (hasta el día anterior sin `Extra`, desde la fecha con él).
- Código: `DistributePayoutAcrossTickets` en `LocalPosRepository.cs`.

Probado con el motor compilado (reflexión sobre los métodos privados reales): 9 casos, incluidos
$10,000 con tarjeta = $608, 3 × $500 efectivo = $68, $150 = −$29, el 18/09 sin descuento, y
TAXI AZUL / TRAVEL sin cambio.

## 9. Campos extra por unidad (para la próxima regla especial)

El negocio pidió dejar campos listos para que la siguiente regla rara se meta como dato y no
como código nuevo. `HardcodedTransportCatalog.Entry` lleva un último parámetro opcional `Extra`
(record `Extras`), todo apagado por omisión:

| Campo | Qué hace | Apagado |
|---|---|---|
| `DescuentoFijoPorLlegada` | Pesos fijos por llegada en lugar de la dejada capturada/tabulada | 0 |
| `UmbralVentaChica` | Desde qué venta del folio se descuenta la dejada; 0 = siempre (puede quedar negativo) | null = regla general $400 |
| `DescuentoExtraPorcentaje` | % de la venta que se quita además de la retención del banco | 0 |
| `BonoFijoPorLlegada` | Pesos que se suman a la comisión por llegada | 0 |

Fórmula: base = venta − retención − % extra − dejada (o fijo) − gastos; comisión = base × % + bono.
Para una regla nueva: cerrar el periodo vigente de la unidad con `EffectiveTo` y agregar otro con
su `Extra`. La pantalla de Configuración de comisiones lo muestra en Notas ("Regla especial: ...").
MAJESTIC ya usa este mecanismo; el cálculo no tiene nada propio de Majestic.

Probado: 15 casos (los 9 de Majestic + los 4 campos, apagados y prendidos).

El detalle de comisión (`PosWindow.LoadCommissionDetail`) ahora dice de dónde sale cada peso:
"Menos el descuento fijo por llegada" ($500 × llegadas) en vez de "dejada del taxista", renglón
del % extra y del bono cuando hay, y el letrero "La compra no llega a $400" sólo sale cuando la
venta de verdad no pasa de $400 (antes salía en un ticket de $8,100 que simplemente no traía
dejada). `LocalCommissionBrowserRow` lleva `DescuentoFijoLlegada`, `DescuentoExtra`,
`DescuentoExtraPorcentaje` y `Bono`.

## 10. Pago de folios con un ticket en negativo (bug corregido)

Caso que planteó el usuario: el ticket más alto no alcanza a cubrir los $500 y los demás son
bajos. Ejemplo Majestic en efectivo, una llegada: $300, $290, $290.
T1 = (270 − 500) × 8 % = **−$18**; T2 = T3 = 261 × 8 % = **$20**; neto del folio **$22**.

**Lo que pasaba:** el botón PAGAR sólo tomaba los tickets con `PuedePagar` (comisión > 0), así
que el diálogo decía $40 y se entregaban $40: el negativo pasaba desapercibido. Además a SQL
(`AppMovilRegistro.pago_comision`) se mandaba la suma con el negativo ($22), y al volver a leer,
`ApplyCommissionPayments` repartía $22 entre los positivos y dejaba un ticket PARCIAL con $18 de
saldo, que se podía volver a pagar. Afectaba a cualquier unidad cuya dejada superara la venta del
ticket mayor, no sólo a Majestic.

**Ahora:**
- `PosWindow.PayCommissionsCoreInnerAsync` paga el **neto del folio** (suma de comisión − pagado de
  todos sus tickets, incluido el negativo). El diálogo muestra "MENOS ticket X en negativo" y el
  ticket impreso incluye el renglón negativo (estatus DESCONTADA). Si el neto es ≤ 0 no se paga y
  se avisa. El respaldo local (`LocalComisiones`) se abona por el neto, del ticket mayor al menor.
- `LocalPosRepository.ApplyCommissionPayments`: si el folio tiene algo pagado, los tickets en
  negativo quedan saldados y lo pagado cubre completos los positivos. Los folios que ya se habían
  pagado con el flujo viejo (que guardaba el neto) dejan de mostrar ese saldo fantasma.

Probado con el motor compilado: sin pagar, neto $22 (antes se cobraban $40); pagado con $22,
ningún ticket queda con saldo.

Paquete: `PLAZA28-DESKTOP-20260919-5.zip` (73.3 MB, 477 archivos, sin credenciales). Sustituye a
todos los anteriores del 19/09 en todas las máquinas; trae también lo de Casco.
