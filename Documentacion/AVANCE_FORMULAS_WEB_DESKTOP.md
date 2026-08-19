# Avance de fórmulas Web migradas a Desktop

Fecha: 26/06/2026.

No se eliminó ni retiró ningún archivo del sistema Web. La migración sigue
conservando el Web como referencia hasta validar con datos reales.

## Comisiones

Se migró al Desktop la fórmula base real del Web usada para `mov_operacion` y
`transporte`.

Referencia Web:

- `Services/PosSqlMirrorService.Comisiones.cs`
- `Services/PosSqlMirrorService.Cortes.cs`

Fórmula portada:

```text
TotalVenta = totaljoyeria + totalcompra
Dejada = dejada
Bebidas = totallicor
Degustacion = totalgastos
Rep = 0
Descuento = totaltarjeta > 0 ? transporte.tarjeta / 100 : transporte.efectivo / 100

ComisionFija =
  transporte.comision > 100 ? transporte.comision :
  transporte.comision <= 0 y transporte.maximo entre 1 y 1000 ? transporte.maximo :
  transporte.comision <= 0 y transporte.minimo entre 1 y 1000 ? transporte.minimo :
  0

Porcentaje =
  si hay comisión fija: 0
  si no: transporte.comision / 100

Comision =
  si ComisionFija > 0:
    ComisionFija
  si Descuento = 0:
    (TotalVenta - Dejada - Bebidas - Rep - Degustacion) * Porcentaje
  si Descuento > 0:
    ((TotalVenta - (TotalVenta * Descuento)) - Dejada - Bebidas - Rep - Degustacion) * Porcentaje

Resultado = truncado a entero, igual que ROUND(valor, 0, 1) / Math.Truncate
```

Implementación Desktop:

- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
  - Cuando existe `mkt__dbo__mov_operacion`, recalcula comisiones desde datos
    reales importados.
  - Cuando no existe, conserva el flujo local temporal para pruebas.

## Cortes

Se migró el cálculo base del Web:

```text
Efectivo = SUM(totalefectivo)
Tarjeta = SUM(totaltarjeta)
Gastos = SUM(totalgastos)
TotalDia = SUM(totaljoyeria + totalcompra + totalartesania + totallicor + totalfarmacia)
Comisiones = SUM(fórmula Web de comisión)
Diferencia = TotalDia - (Efectivo + Tarjeta)
Movimientos = COUNT(mov_operacion)
Cerrado = existe cierre en ControlTaxis.Cortes
```

Implementación Desktop:

- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
  - Si existe `mkt__dbo__mov_operacion`, calcula corte desde datos importados
    usando la fórmula real.
  - Si existe `ControlTaxis__dbo__Cortes`, toma el estado cerrado.

## Importador preparado para datos reales

Se agregó comando:

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- normalize-pos --db .\DatosLocal\ControlTaxi.db
```

Qué hace:

- Lee `mkt__dbo__mov_operacion`.
- Usa `mkt__dbo__transporte` si existe.
- Genera/actualiza:
  - `LocalComisiones`
  - `LocalCortes`
- Aplica la fórmula Web exacta base para comisiones/cortes.

## Estado

| Módulo | Estado técnico | Validación real |
|---|---|---|
| Comisiones | Fórmula Web base migrada para datos importados. | Pendiente hasta tener `DatosLocal/ControlTaxi.db` real. |
| Cortes | Fórmula Web base migrada para datos importados. | Pendiente hasta tener `DatosLocal/ControlTaxi.db` real. |
| Reportes especializados | Pendiente. | Pendiente. |
| Excel/PDF Web exacto | Pendiente. | Pendiente. |
| Usuarios/permisos | Implementado parcial. | Pendiente. |
| Gafetes | Implementado parcial. | Pendiente. |

## Avance adicional: reportes, usuarios y gafetes

Se agregó soporte Desktop offline para:

- reportes especializados desde SQLite importado;
- exportación `.xlsx` OpenXML local;
- administración visual de usuarios/permisos;
- lectura/exportación de movimientos importados de gafetes.

Detalle completo:

- `AVANCE_REPORTES_USUARIOS_GAFETES_DESKTOP.md`

Estado: implementado técnicamente; pendiente validación con
`DatosLocal/ControlTaxi.db`.
