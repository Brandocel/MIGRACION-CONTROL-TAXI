# Avance POS crítico Desktop

Fecha: 26/06/2026.

Este avance usa `MATRIZ_PARIDAD_MODULOS.md` como guía y no elimina ni retira
ningún archivo del sistema Web. La lógica Web sigue conservada como referencia.

## Implementado en WPF + SQLite local

| Prioridad | Módulo | Implementación Desktop | Estado |
|---|---|---|---|
| 1 | Ventas | Catálogo local de productos, alta/actualización, creación transaccional de venta con líneas, descuento de existencia, subtotal, IVA, total y folio único. | Implementado localmente; pendiente validación con datos reales. |
| 2 | Pagos | Registro de pagos por folio de venta, validación de importe positivo, bloqueo de pago excedido y actualización de estado/saldo. | Implementado localmente; pendiente validación con pagos reales. |
| 3 | Comisiones | Recalculo local desde ventas, generación de comisión, saldo, estatus pendiente/parcial/pagada y abonos. | Implementado con fórmula temporal comparable; pendiente portar fórmula exacta del Web. |
| 4 | Cortes | Cálculo por fecha con efectivo, tarjeta, pagos, esperado, contado, diferencia y cierre. | Implementado localmente; pendiente integrar gastos/reglas exactas. |
| 5 | Reportes | Listados WPF de ventas, pagos, comisiones, cortes y auditoría. | Implementado localmente; pendiente reportes especializados. |
| 6 | Exportaciones Excel/PDF | Exportación Excel local SpreadsheetML (`.xls`) y PDF básico generado sin paquetes externos. | Implementado como base; pendiente igualar plantillas Web. |
| 8 | Auditoría | Tabla `LocalAuditoria` y registro transaccional de producto, venta, pago, comisión y corte. | Implementado localmente; pendiente comparar contra `AuditoriaMovimiento`. |

## Archivos creados

- `ControlTaxiDesktop/Models/LocalPosModels.cs`
- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
- `ControlTaxiDesktop/PosWindow.xaml`
- `ControlTaxiDesktop/PosWindow.xaml.cs`
- `AVANCE_POS_CRITICO_DESKTOP.md`

## Archivos modificados

- `ControlTaxiDesktop/Services/LocalDatabase.cs`
- `ControlTaxiDesktop/Services/DesktopOutputService.cs`
- `ControlTaxiDesktop/MainWindow.xaml.cs`
- `ControlTaxiDesktop/App.xaml.cs`

## Validación ejecutada

- `dotnet build 'CONTROL TAXI.sln' --no-restore`
  - Resultado: correcto, 0 errores, 0 advertencias.
- `dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test`
  - Resultado: correcto.
  - Valida SQLite local, login temporal, registros existentes y flujo nuevo:
    producto, venta, pago, recalculo de comisión, abono, corte y auditoría.
- `dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false`
  - Resultado: correcto.
  - Ejecutable publicado en
    `ControlTaxiDesktop\bin\Release\net9.0-windows\win-x64\publish\ControlTaxiDesktop.exe`.
- `ControlTaxiDesktop.exe --self-test`
  - Resultado: el proceso publicado ejecutó la prueba y cerró; no quedaron
    instancias activas de `ControlTaxiDesktop.exe`.

## Pendiente para marcar como validado

Nada de este avance se marca como “validado” ni “listo para retirar del Web”
hasta importar datos reales y comparar contra el Web:

- mismos folios;
- mismos productos/remisiones;
- mismos pagos y saldos;
- mismas fórmulas de comisión;
- mismos cortes, gastos y diferencias;
- mismos archivos Excel/PDF;
- mismas entradas de auditoría.
