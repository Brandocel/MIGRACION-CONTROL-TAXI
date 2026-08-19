# Avance reportes, usuarios y gafetes Desktop

Fecha: 26/06/2026.

No se eliminó ni retiró ningún archivo del sistema Web.

## Implementado en esta fase

### Reportes especializados

Se agregaron consultas offline para SQLite importado:

- Reporte taxi.
- Reporte dejadas/concentrado base.
- Pagos/comisiones.
- Gafetes.

Referencia Web consultada:

- `Controllers/PosController.cs`
- `Services/PosSqlMirrorService.Relaciones.cs`
- `Models/PosTriton/PosReportesViewModel.cs`

Implementación Desktop:

- `ControlTaxiDesktop/Models/LocalReportModels.cs`
- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
- `ControlTaxiDesktop/PosWindow.xaml`
- `ControlTaxiDesktop/PosWindow.xaml.cs`

Estado: implementado técnicamente para datos importados; pendiente validación
con `DatosLocal/ControlTaxi.db`.

### Excel/PDF

Se agregó exportación `.xlsx` OpenXML local sin servidor, navegador ni paquetes
externos.

Implementación:

- `ControlTaxiDesktop/Services/DesktopOutputService.cs`

Los botones nuevos generan:

- `Reporte_Taxi.xlsx`
- `Control_Dejadas.xlsx`
- `Concentrado_General.xlsx`
- `Pagos_Comisiones.xlsx`
- `Gafetes.xlsx`

Estado: columnas y nombres alineados con el Web base; pendiente comparación
visual/celda por celda con datos reales.

### Usuarios y permisos

Se agregó administración visual WPF:

- usuarios;
- rol;
- estatus;
- contraseña local PBKDF2;
- permisos por módulo/pantalla usando el catálogo Web:
  `RegistroDiario`, `Comisiones`, `Transportes`, `Guias`, `Taxistas`,
  `Gafetes`, `Relaciones`, `Gastos`, `Cortes`, `Reportes`, `ReporteTaxis`,
  `ControlDejadas`, `DejadasComisiones`, `ConcentradoGeneral`, `Usuarios`.

Implementación:

- `ControlTaxiDesktop/UserAdminWindow.xaml`
- `ControlTaxiDesktop/UserAdminWindow.xaml.cs`
- `ControlTaxiDesktop/Services/LocalUserRepository.cs`
- `ControlTaxiDesktop/MainWindow.xaml.cs`

Estado: implementado localmente; pendiente validar con usuarios reales
importados.

### Gafetes

Se agregó lectura offline de movimientos importados desde `mkt__dbo__gafete`
con columnas equivalentes:

- número;
- staff;
- folio operación;
- fecha entrega;
- estatus `OCUPADO`, `SUSPENDIDO`, `LIBRE`;
- regreso.

Estado: implementado para datos importados; pendiente enriquecer con
`AppMovilRegistro` y validar contra datos reales.

## Validación ejecutada

- `dotnet build 'CONTROL TAXI.sln' --no-restore`
  - Correcto: 0 errores, 0 advertencias.
- `dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test`
  - Correcto.
- `dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db ControlTaxiDesktop\bin\Debug\net9.0-windows\DatosLocal\ControlTaxi.prueba.db --report VALIDACION_PARIDAD_DATOS_REALES.md`
  - Correcto.
- `dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false`
  - Correcto.
- `ControlTaxiDesktop.exe --self-test`
  - Correcto; no quedaron instancias activas.

## Pendiente

- Validar con `DatosLocal/ControlTaxi.db`.
- Completar ramas enriquecidas App/Dejadas con todas las columnas si la base real
  contiene las vistas/tablas necesarias.
- Comparar Excel/PDF contra archivos generados por el Web.
- Comparar permisos reales de usuarios importados.
- Comparar movimientos de gafetes con el historial real.

