# Avance funcional WPF

## Verificado localmente

El ejecutable WPF publicado se prueba con `--self-test`. La prueba no usa red y
crea/usa únicamente `DatosLocal/ControlTaxi.prueba.db`. Verifica:

- autenticación local PBKDF2 (`admin/admin` solo en la base temporal);
- alta y consulta de tarifa;
- alta y consulta de hotel;
- alta y consulta de taxista;
- alta y consulta de gafete;
- alta y consulta de registro;
- cálculo de total, efectivo y tarjeta del reporte local.

## Pantallas migradas en esta fase

| Módulo | Implementación WPF actual | Falta para paridad completa |
|---|---|---|
| Registros | Alta y listado local SQLite. | Edición, búsqueda avanzada, folios reales, exportación e impresión. |
| Tarifas | Alta/actualización y listado. | Reglas de tarifas históricas y equivalencias importadas. |
| Hoteles | Alta/reactivación y listado. | Importación y validación frente a catálogos reales. |
| Taxistas | Alta/actualización y listado. | Búsqueda, paginación y todos los datos del catálogo original. |
| Gafetes | Alta y listado. | Asignación, devolución, masivos y auditoría. |
| Reportes | Concentrado local de registros por rango. | Reportes Excel/PDF/CSV, cuadre, comisiones y formatos existentes. |
| Usuarios/permisos | Login local y filtrado del menú por permiso. | Administración visual de usuarios, edición de permisos y auditoría. |

## Archivos creados o modificados

- `ControlTaxiDesktop/Services/LocalDatabase.cs`: esquema SQLite local.
- `ControlTaxiDesktop/Services/LocalUserRepository.cs`: autenticación y permisos.
- `ControlTaxiDesktop/Services/LocalOperationsRepository.cs`: persistencia local de
  los módulos operativos iniciales.
- `ControlTaxiDesktop/Models/DesktopSession.cs` y `LocalOperationsModels.cs`:
  modelos Desktop.
- `ControlTaxiDesktop/MainWindow.xaml` y `.cs`: acceso, permisos y navegación.
- `ControlTaxiDesktop/OperationsWindow.xaml` y `.cs`: formularios/listados WPF.
- `ControlTaxiDesktop/App.xaml.cs`: autotest local reproducible.

El sistema web y su lógica se conservan temporalmente como referencia hasta
completar las filas marcadas como pendientes y validar los datos reales.
