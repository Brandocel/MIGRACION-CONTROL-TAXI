# Contexto de trabajo — 25 de septiembre de 2026

Revisión y reparación de la rama **Alejandro** (PR #1: "Fase 1/2/3" de comisiones de Casco Viejo).
El trabajo quedó en la rama `arreglos-alejandro`, encima de los 3 commits de Alejandro.

Continuación de `CONTEXTO-SESION-2026-09-24.md`.

---

## 1. Qué traía la rama

Tres cosas buenas: comisiones separadas por sucursal en la base local, dejada de Casco por
cantidad de adultos, y degustación por regla. El problema no era la idea, era el alcance: tocaba
código compartido con Plaza 28 y dejaba trabajo anterior tirado.

## 2. Lo que se reparó

1. **El build de Debug no compilaba en ninguna máquina.** El `.csproj` exigía
   `branches.local.json`, que no está en el repositorio: `error MSB3030`. Ahora la copia lleva
   `Exists(...)`, así que solo se copia si el archivo está.
2. **Las 14 reglas de comisión de Casco del 19/09 quedaban muertas.** El cálculo pasó de leer
   `dbo.ControlTaxiComisiones` (SQL Server, compartido) a leer SQLite (local, por máquina), y
   SQLite estaba vacío: toda comisión de Casco habría salido pendiente. Se agregó
   `Services/CascoCommissionRuleImporter.cs`, que la primera vez que la máquina lo necesita trae
   esas reglas de SQL Server y las traduce al formato nuevo. Nunca pisa lo capturado a mano.
3. **Se había quitado el 10 % de respaldo de Casco.** Volvió: sin regla configurada se calcula el
   10 % de la venta, como antes, y el diagnóstico anota que falta configurar esa regla.
4. **Las comisiones ya generadas y pagadas se recalculaban.** Volvió a respetarse lo que ya está
   generado en `dbo.ControlTaxiComisionesGeneradas`: ese importe manda sobre cualquier recálculo.
5. **`branches.local.json` mandaba también en Release.** Un archivo olvidado junto al .exe apuntaba
   el programa a otra base sin avisar. Ahora solo se busca en Debug (`#if DEBUG`).
6. **El arranque de Casco ya no se detenía sin credencial.** Volvió el aviso y el cierre: entrar sin
   credencial solo servía para que cada pantalla fallara después con un error que no dice nada.
7. **Andamio de pruebas dentro del programa.** Se quitó el bloqueo de `LocalUserRepository` que
   exigía `.\SQLEXPRESS`/`mkt`, y el de `UserAdminWindow` con sus ventanas de "conexión efectiva"
   (ese corría también en Release). El diagnóstico `--diag-branches` dejó de abrir dos ventanas.
8. **`.Single(...)` tronaba la pantalla.** Con cero o dos reglas vigentes salía una excepción sin
   atrapar. Ahora es `SingleOrDefault` con aviso, y dos reglas vigentes dan "REGLA_AMBIGUA_CV".
9. **El transporte no empataba con la regla.** El motor nuevo comparaba el texto tal cual
   ("VAN BLANCA 7914") contra el nombre de la regla ("TAXIS/VANS"). Ahora en Casco se compara
   también contra el proveedor normalizado, que es como el negocio tiene sus reglas.
10. **Faltaba el descuento especial de AVENTURAS MAYAS** ($100 por cada $1,000 desde $1,000). Sin
    él la comisión de ese proveedor salía más alta de lo que el negocio paga.
11. **Casco redondea, Plaza 28 trunca.** El motor nuevo truncaba en las dos; se dejó el redondeo a
    dos decimales en Casco para no mover centavos contra lo ya pagado.
12. **El POS calculaba Casco con el catálogo fijo de Plaza 28** (`LocalPosRepository` con sucursal
    vacía). Ahora recibe la sucursal de la sesión.
13. **El POS decía "Pendiente" encima de un importe real.** Muestra el número; el aviso de que se
    usó el respaldo va en el detalle.
14. **`Tools/CommissionTastingChecks` no compilaba** (NU1903 como error). Se le pusieron las mismas
    supresiones que el programa principal.

## 3. Pruebas hechas

Sobre una copia de la base real, con el motor ya arreglado:

| Caso | Resultado |
| --- | --- |
| Plaza 28, VAN $1,000 efectivo | $100.00, sin cambio |
| Migración de la tabla de reglas | 46 reglas antes, 46 después |
| Casco sin reglas | $100.00 por respaldo del 10 % |
| Importación de reglas de SQL Server | 4 proveedores de prueba traducidos y guardados |
| TAXIS/VANS tarjeta $1,000, dejada $200 | $61.00 |
| TAXIS/VANS efectivo $1,000, dejada $200 | $80.00 |
| MAJESTIC efectivo $1,000 (19 % siempre, sin dejada) | $97.20 |
| BIKE CID efectivo $1,000, dejada $100 | $90.00 |
| AVENTURAS MAYAS tarjeta $2,000 (descuento $200) | $170.40 |
| Transporte desconocido | $100.00 por respaldo |
| TAXIS/VANS, 3 adultos (banda $350) | $65.00 |
| TAXIS/VANS, 6 adultos (banda $500) | $50.00 |
| TAXIS/VANS sin adultos confirmados | usa la dejada del viaje, $80.00 |

Los nueve casos de Casco dan el mismo número que el motor anterior del 19/09. Release y Debug
compilan, y los cuatro proyectos de `Tools/` también.

## 4. Cómo quedó la dejada por adultos

Es lo nuevo que aporta la rama y se conservó, pero sin bloquear:

- Si la regla no descuenta dejada, no se descuenta nada.
- Si descuenta y hay adultos confirmados y bandas capturadas, se usa la banda (1–4 / 5 o más).
- Si faltan los adultos o las bandas, se usa la dejada que trae el viaje y se anota en el detalle.
  Antes, sin esos datos, la comisión se quedaba en cero.

## 5. Los gafetes y las comisiones ya viven juntos

El trabajo de gafetes del 24/09 estaba sin commit y tocaba los mismos archivos. Se junto en
`main` (commits `de31b10` y `653a757`). El unico conflicto fue `CascoCommissionRuleService`: la
rama de gafetes traia el motor viejo de Casco y `main` ya lo habia reemplazado por el que lee
SQLite. **Se quedo el de `main`**; el 10 % de respaldo no se perdio, ahora vive en
`CommissionConfigurationResolver`. El importador tambien pasa por `CascoSqlIdentity`, asi que ya
no queda ningun usuario de SQL escrito a mano.

Despues de juntar se volvio a probar todo sobre la base local: los nueve casos de comision dan el
mismo importe, y los tres viajes de prueba siguen sacando sus talones (3, 1 y 2 gafetes).

## 6. Las comisiones de Casco quedaron fijas en el programa, como Plaza 28

Decisión del negocio el 25/09: las reglas de Casco dejan de vivir en la base de cada máquina y
pasan a ser catálogo fijo del programa, igual que `HardcodedTransportCatalog` de Plaza 28. Así
todas las máquinas cobran lo mismo desde el día que se instala, y ya no hace falta importar nada
de SQL Server (se borró `CascoCommissionRuleImporter`).

`Services/HardcodedCascoCommissionCatalog.cs` sale del Excel del negocio
(`CALCULO DE COMISIONES, PTO MORELOS.xlsx`, hojas CASCO, DEJADA y DEGUSTACION):

| Proveedor | Comisión | Retención | Dejada | Gasto | Dejada por adultos |
| --- | --- | --- | --- | --- | --- |
| BIKE CID | 10 % agencia | 19 % con tarjeta | sí | sí | $50 / $100 |
| TAXIS/VANS | 10 % taxista | 19 % con tarjeta | sí | sí | $150 / $200 |
| UBER | 10 % | 19 % con tarjeta | sí | sí | $100 siempre |
| UBER + | 10 % | 19 % con tarjeta | sí | sí | $150 siempre |
| CALLE | 0 % | 19 % con tarjeta | no | sí | — |
| EXTREME | 10 % guía | 19 % con tarjeta | no | sí | — |
| AVENTURAS MAYAS | 12 % (10 guía + 2 agencia) | 19 % con tarjeta | no | sí | — |
| MAJESTIC | 12 % (8 guía + 4 agencia) | **19 % siempre** | no | sí | — |
| VENTAS ENTRE TIENDAS | 50 % Matilde | 19 % con tarjeta | no | sí | — |

Además: AVENTURAS MAYAS descuenta $100 por cada $1,000 de venta desde $1,000, y la **degustación
de joyería se calcula sola** ($100 hasta $20,000 de venta de joyería, $200 de ahí en adelante),
porque el Excel dice que en joyería siempre se quita. La de licores se sigue capturando: depende
de si la venta trae algún artículo de licor, y eso el sistema no lo ve.

CALLE queda en 0 % a propósito: en el Excel esa venta solo paga comisión de vendedor, que está
fuera del alcance.

**FARMACIAS quedó pendiente**: el Excel dice "20 % y/o 10 %" según si el medicamento es
controlado, y falta que el negocio entregue la lista de controlados. Mientras tanto no tiene regla
y cae en el respaldo del 10 %, que es como se venía calculando.

Al ser catálogo fijo, la pantalla de Configuración de comisiones vuelve a mostrar las reglas de
transporte como solo lectura, también en Casco.

### Pruebas

Quince casos sacados a mano del Excel, sobre una copia de la base real: BIKE CID, TAXIS/VANS,
UBER, UBER +, EXTREME, MAJESTIC (con y sin tarjeta), AVENTURAS MAYAS (con y sin descuento
especial), VENTAS ENTRE TIENDAS, CALLE, FARMACIAS por respaldo y un caso con gasto capturado.
Todos cuadran, Plaza 28 sigue dando $100.00 en su caso de control, y los tres viajes de prueba
siguen sacando sus talones de gafete.

Se corrigió de paso el empate de nombres: UBER tiene regla propia y a la vez normaliza a
TAXIS/VANS, así que empataban las dos y salía "regla ambigua". Ahora el nombre exacto manda sobre
el normalizado.

## 7. Pendientes

1. Probar en la máquina de Casco: la importación corre sola la primera vez, hay que confirmar que
   trae las 14 reglas y que las comisiones salen iguales a las de hoy.
2. Decidir si las reglas de Casco se quedan en SQLite (por máquina) o vuelven a compartirse. Hoy,
   si alguien edita una regla en una máquina, las demás no se enteran.
3. FARMACIAS: falta la lista de medicamentos controlados para saber cuándo va 20 % y cuándo 10 %.
4. La comisión de vendedor y la deportiva (40/45 de meta, 30 %, 35 %, 50 %) siguen fuera.
5. La hoja MATILDE del Excel: son otras 4 reglas, y falta definir cómo distingue el sistema una
   venta de Matilde de una de Casco.
4. Juntar esto con los gafetes impresos del 24/09, que tocan los mismos archivos.
