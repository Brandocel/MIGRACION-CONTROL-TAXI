# Paridad Web vs Desktop

Estado: **parcial; no autorizada la retirada del sistema Web**.

## Funciones migradas y verificadas en Desktop

- Inicio WPF nativo y autenticación local PBKDF2.
- Base SQLite local y modo temporal de prueba claramente identificado.
- Alta/listado de registros, tarifas, hoteles, taxistas y gafetes.
- Asignación y devolución local de gafetes.
- Reporte local básico por fechas: conteo, total, efectivo y tarjeta.
- Exportación CSV de registros e impresión nativa del resumen.
- Autotest local de SQLite, autenticación, altas y cálculo (`--self-test`).

Estas funciones **no son todavía equivalentes al 100%** de sus versiones Web y no
se marcan como listas para retirar.

## Funciones pendientes de paridad

- Exportaciones Excel con los cinco formatos existentes: taxi, dejadas,
  concentrado, cuadre final y pagos/comisiones.
- Exportaciones PDF de registro, pagos, comisiones y corte con el mismo diseño.
- Ventas, remisiones, pagos, gastos, cortes, relaciones, dejadas y comisiones.
- Reportes especializados y sus cálculos SQL actuales.
- Administración visual completa de usuarios/permisos y auditoría local.
- Validaciones específicas por módulo, impresión de tickets y carga de archivos.
- Importación de datos reales y comparación de conteos/resultados contra SQL
  Server y Hostinger histórico.

## Diferencias detectadas

El Web contiene 15 acciones de exportación y lógica de negocio distribuida en
`PosController.cs` y servicios `PosSqlMirrorService.*`. El Desktop actual solo
presenta un modelo local inicial y un CSV de registros. Por eso no existe base
técnica para afirmar que formatos, cálculos o resultados coincidan todavía.

## Archivos Web que pueden retirarse

Ninguno por ahora. La paridad aún no está confirmada.

## Archivos que deben conservarse como referencia

- `Controllers/PosController.cs`: exportaciones, PDF simple, rutas y validación.
- `Services/PosSqlMirrorService.*.cs`: cálculos, auditoría y consultas de cada
  módulo.
- `Views/Pos/**/*.cshtml`: formatos y campos existentes.
- `Models/PosTriton/`, `Data/`, `Interfaces/` y scripts SQL: contratos y esquema.
- `Fix-ExcelReports.ps1`: reparación histórica de archivos Excel hasta validar
  los nuevos exportadores.

## Condición para retirar Web

Cada función debe tener prueba de regresión sobre datos importados reales:
conteos, importes, permisos, archivo generado, impresión y resultado esperado.
Solo entonces se actualizará este documento a “lista para retirar”.
