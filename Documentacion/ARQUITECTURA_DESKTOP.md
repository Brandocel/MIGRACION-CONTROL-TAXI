# Arquitectura Desktop

Fecha: 2026-06-29.

## Vista general

```text
Usuario
  |
  v
WPF: ControlTaxiDesktop.exe
  |
  v
Repositorios locales
  |
  v
SQLite: DatosLocal\ControlTaxi.db
```

No existe servidor Web, navegador, WebView, IIS, localhost ni dependencia de Internet.

## Capas

| Capa | Archivos | Responsabilidad |
|---|---|---|
| UI WPF | `MainWindow`, `OperationsWindow`, `PosWindow`, `UserAdminWindow` | Pantallas nativas Windows. |
| Servicios/repositorios | `Services/*.cs` | Acceso a datos, validaciones locales, reportes y exportacion. |
| Modelos | `Models/*.cs` | Contratos internos de datos. |
| Base local | `DatosLocal\ControlTaxi.db` | Persistencia SQLite. |
| Herramientas | `ControlTaxiDesktop.Tools` | Importar, normalizar y validar datos reales. |

## Flujo de datos

1. El usuario abre el `.exe`.
2. `LocalDatabase` crea o abre SQLite.
3. `LocalUserRepository` autentica y carga permisos.
4. Las ventanas WPF llaman repositorios locales.
5. Los repositorios consultan/guardan en SQLite.
6. Exportaciones e impresion se generan localmente.

## Importacion

Los datos reales se importan con `ControlTaxiDesktop.Tools`.

Fuentes soportadas:

- SQL Server local;
- `.bak`;
- `.csv`;
- `.xlsx`;
- `.json`;
- `.sqlite` / `.db`.

## Offline

La aplicacion Desktop no debe consumir:

- Hostinger;
- APIs remotas;
- CDN;
- servidor Web;
- localhost;
- navegador;
- WebView.

## Mantenimiento

- Mantener respaldos de SQLite.
- Ejecutar `validate-parity` despues de importaciones.
- No modificar reglas de comisiones/cortes sin comparar contra Web.
- Mantener el Web como referencia hasta retiro formal.

