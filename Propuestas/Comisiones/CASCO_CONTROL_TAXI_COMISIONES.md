# ControlTaxiComisiones para Casco Viejo

## Objetivo

Dejar las comisiones de Casco Viejo como configuracion SQL escalable, sin porcentajes hardcodeados en C#.

Arquitectura objetivo:

`RemisioM -> Venta real -> dbo.ControlTaxiComisiones -> calculo -> vista Relacion Ticket-Taxista`

## Alcance de esta fase

En esta etapa solo se hizo:

- revisar el Excel `CALCULO DE COMISIONES, PTO MORELOS (1).xlsx`
- identificar reglas de comision para Casco
- preparar el script de estructura
- preparar una carga inicial idempotente
- dejar todas las reglas inicialmente inactivas (`Activo = 0`)
- marcar reglas ambiguas con `RequiereValidacion = 1`

No se activo todavia:

- calculo de comisiones
- aplicacion sobre operaciones reales
- cambios en la columna Comision
- cambios a Plaza 28
- cambios a pagos, gafetes, sincronizador o reportes ya cerrados

## Venta en Relacion Ticket-Taxista

La columna `Venta` ya quedo separada del tema de comisiones.  
Para Casco Viejo se obtiene por `folio_operacion` desde:

- `compuadmoCasco.dbo.remisioM` en local
- `joyeriaCasco.dbo.remisioM` en local

Y en produccion debera consultar:

- `compuadmo.dbo.remisioM`
- `joyeria.dbo.remisioM`

Si no encuentra remision, la vista muestra `0.00`.

## Hoja revisada

Archivo:

- `C:\Users\reyna\Downloads\CALCULO DE COMISIONES, PTO MORELOS (1).xlsx`

Hojas encontradas:

- `CASCO`

## Estructura SQL propuesta

Archivo:

- `Propuestas\Comisiones\CASCO_CONTROL_TAXI_COMISIONES.sql`

Columnas principales:

- `BranchCode`
- `Proveedor`
- `TipoServicio`
- `ConTarjeta`
- `VentaMinima`
- `VentaMaxima`
- `ComisionAgencia`
- `ComisionTaxista`
- `ComisionVendedor`
- `ComisionDeportiva`
- `AplicaDegustacion`
- `AplicaGasto`
- `Activo`
- `ReglaNombre`
- `OrigenExcel`
- `RequiereValidacion`
- `Observaciones`

## Resumen de reglas detectadas

- Total de reglas encontradas en el Excel: `18`
- Reglas convertidas a SQL: `18`
- Reglas claras: `2`
- Reglas ambiguas o pendientes: `16`
- Hojas revisadas: `1`
- Hojas excluidas: `0`

## Tabla de reglas

| Regla | Transporte | Condicion | Venta minima | Venta maxima | Agencia | Taxista/Guia | Vendedor | Estado |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| BIKE CID C/TARJETA | BIKE CID | Con tarjeta | 0 | null | 19% | Pendiente | Pendiente | Pendiente |
| BIKE CID S/TARJETA | BIKE CID | Sin tarjeta | 0 | null | Pendiente | Pendiente | Pendiente | Pendiente |
| TAXIS/VANS C/TARJETA | TAXIS/VANS | Con tarjeta | 0 | null | 19% | 10% | Pendiente | Pendiente |
| TAXIS/VANS S/TARJETA | TAXIS/VANS | Sin tarjeta | 0 | null | Pendiente | 10% | Pendiente | Pendiente |
| CALLE C/TARJETA | CALLE | Con tarjeta | 0 | null | 19% | Pendiente | Pendiente | Pendiente |
| CALLE S/TARJETA | CALLE | Sin tarjeta | 0 | null | Pendiente | Pendiente | Pendiente | Pendiente |
| EXTREME C/TARJETA | EXTREME | Con tarjeta | 0 | null | Pendiente | 10% | Pendiente | Pendiente |
| EXTREME S/TARJETA | EXTREME | Sin tarjeta | 0 | null | Pendiente | 10% | Pendiente | Pendiente |
| AVENTURAS MAYAS C/TARJETA + META | AVENTURAS MAYAS | Con tarjeta y ventas arriba de 1000 | 1000 | null | 2% | 10% | Pendiente | Pendiente |
| AVENTURAS MAYAS S/TARJETA + META | AVENTURAS MAYAS | Sin tarjeta y ventas arriba de 1000 | 1000 | null | 2% | 10% | Pendiente | Pendiente |
| MAJESTIC C/TARJETA | MAJESTIC | Con tarjeta | 0 | null | 4% | 8% | Pendiente | Validada |
| MAJESTIC S/TARJETA | MAJESTIC | Sin tarjeta | 0 | null | 4% | 8% | Pendiente | Validada |
| FARMACIAS C/TARJETA ANTIBIOTICOS | TAXIS Y GUIAS IND (FARMACIAS) | Antibioticos con tarjeta | 0 | null | 19% | Pendiente | Pendiente | Pendiente |
| FARMACIAS S/TARJETA CONTROLADOS | TAXIS Y GUIAS IND (FARMACIAS) | Controlados sin tarjeta | 0 | null | Pendiente | Pendiente | Pendiente | Pendiente |
| VENTAS ENTRE TIENDAS C/TARJETA | VENTAS ENTRE TIENDAS | Interdepartamental con tarjeta | 0 | null | 19% | Pendiente | 20% | Pendiente |
| VENTAS ENTRE TIENDAS S/TARJETA | VENTAS ENTRE TIENDAS | Interdepartamental sin tarjeta | 0 | null | Pendiente | Pendiente | 20% | Pendiente |
| META DIARIA JOYERIA | META DIARIA | Joyeria | 10000 | null | Pendiente | Pendiente | Pendiente | Pendiente |
| META DIARIA TEQ/ART | META DIARIA | Teq/Art | 7000 | null | Pendiente | Pendiente | Pendiente | Pendiente |

## Reglas claras

Las dos reglas que el Excel deja mas claras son:

1. `MAJESTIC C/TARJETA`
   - comision guia `8%`
   - comision agencia `4%`
   - aplica gasto
   - aplica degustacion
   - deportiva `30`

2. `MAJESTIC S/TARJETA`
   - comision guia `8%`
   - comision agencia `4%`
   - aplica gasto
   - aplica degustacion
   - deportiva `30`

## Reglas ambiguas

Se marcaron con `RequiereValidacion = 1` porque el Excel no deja resuelto de forma unica alguno de estos puntos:

- si el 10% pertenece a taxista, guia o vendedor
- si el 19% es agencia en todas las variantes
- como se representa `40 o 45 (meta)`
- como se aplica el descuento de `100 pesos por cada 1000`
- si `20% y/o 10%` corresponde a controlados, antibioticos o a diferentes actores
- si `50%` en ventas entre tiendas corresponde a Matilde, agencia o deportiva
- si las metas diarias generan comision o solo son referencia

## Reglas excluidas

No se excluyo ninguna hoja ni bloque del Excel.  
Lo que no quedo suficientemente claro se convirtio en registro inactivo y pendiente de validacion, sin activar calculos.

## Archivos generados o actualizados

- `Propuestas\Comisiones\CASCO_CONTROL_TAXI_COMISIONES.sql`
- `Propuestas\Comisiones\CASCO_CONTROL_TAXI_COMISIONES_SEED.sql`
- `Propuestas\Comisiones\CASCO_CONTROL_TAXI_COMISIONES_VALIDACION.sql`
- `Propuestas\Comisiones\CASCO_CONTROL_TAXI_COMISIONES.md`

## Confirmacion

El calculo de comisiones todavia no fue activado.  
Solo quedo preparada la configuracion inicial para Casco Viejo.
