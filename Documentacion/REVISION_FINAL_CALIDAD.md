# Revisión final de calidad Desktop

Fecha: 2026-06-30.

Alcance: revisión técnica del proyecto `ControlTaxiDesktop` y sus rutas de ejecución/publicación, usando únicamente el código existente. No se eliminó el proyecto Web, no se modificó SQL Server y no se cambió lógica de negocio.

## Resultado ejecutivo

El proyecto Desktop compila, ejecuta self-test, valida paridad, corre pipeline y publica correctamente. No se encontraron ventanas huérfanas, botones principales sin manejador, servicios completos sin uso ni tablas SQLite locales creadas sin propósito funcional.

Se corrigió un riesgo de instalación: el self-test generaba una base temporal `ControlTaxi.prueba.db` dentro de la carpeta de ejecución. Ahora las pruebas temporales usan una carpeta única en `%TEMP%`, por lo que no ensucian `Release`, `bin` ni una instalación en otra computadora.

## 1. Clases, ventanas, servicios, repositorios o archivos sin uso

| Elemento revisado | Estado | Acción |
|---|---|---|
| `MainWindow` | Usado por `App.xaml` como ventana inicial. | Conservado. |
| `OperationsWindow` | Se abre desde módulos operativos: registros, tarifas, hoteles, taxistas, gafetes, transportes, guías, gastos, relaciones y reportes. | Conservado. |
| `PosWindow` | Se abre desde ventas, comisiones, cortes y reportes. | Conservado. |
| `PortalWindow` | Se abre desde el módulo Portal. | Conservado. |
| `UserAdminWindow` | Se abre desde Usuarios. | Conservado. |
| `ChecklistValidacionFinal` | Se abre desde el botón `Checklist validación`. | Conservado. |
| `LocalDatabase` | Inicialización/rutas/SQLite. | Conservado. |
| `LocalOperationsRepository` | Usado por `OperationsWindow` y self-test. | Conservado. |
| `LocalPosRepository` | Usado por `PosWindow` y self-test. | Conservado. |
| `LocalPortalRepository` | Usado por `PortalWindow` y self-test. | Conservado. |
| `LocalUserRepository` | Usado por login, usuarios y self-test. | Conservado. |
| `LocalErrorLogger` | Usado en ventanas críticas para auditoría local de errores. | Conservado. |
| `DesktopOutputService` | Usado por exportaciones, PDF, Excel, CSV e impresión. | Conservado. |

Hallazgo especial:

| Elemento | Observación | Decisión |
|---|---|---|
| `LocalPosRepository.GetSaleLinesAsync` y `LocalSaleLine` | No están conectados a un botón actual, pero representan líneas de venta/remisión. Eliminarlos reduciría soporte para detalle de remisión/productos. | Conservados. No se consideran código muerto seguro. |

## 2. Botones del menú que no abran pantalla

Revisión del menú principal:

| Botón / módulo | Destino | Estado |
|---|---|---|
| Registro diario | `OperationsWindow` | Correcto. |
| Registro aplicación móvil | `OperationsWindow` | Correcto. |
| Ventas | `PosWindow` | Correcto. |
| Comisiones | `PosWindow` | Correcto. |
| Transportes | `OperationsWindow` | Correcto. |
| Guías | `OperationsWindow` | Correcto. |
| Taxistas | `OperationsWindow` | Correcto. |
| Gafetes | `OperationsWindow` | Correcto. |
| Relaciones | `OperationsWindow` | Correcto. |
| Gastos | `OperationsWindow` | Correcto. |
| Cortes | `PosWindow` | Correcto. |
| Reportes | `PosWindow` | Correcto. |
| Usuarios | `UserAdminWindow` | Correcto. |
| Portal | `PortalWindow` | Correcto. |
| Checklist validación | `ChecklistValidacionFinal` | Correcto. |

No se encontraron botones principales sin apertura de pantalla.

## 3. Pantallas que no puedan abrirse desde el menú

| Pantalla | Acceso |
|---|---|
| `MainWindow` | Inicio de aplicación. |
| `OperationsWindow` | Módulos operativos. |
| `PosWindow` | Ventas/comisiones/cortes/reportes. |
| `PortalWindow` | Módulo Portal. |
| `UserAdminWindow` | Módulo Usuarios. |
| `ChecklistValidacionFinal` | Botón `Checklist validación`. |

No se encontraron pantallas WPF huérfanas.

## 4. Consultas SQLite no utilizadas

No se eliminaron consultas. La única consulta detectada como no invocada por UI directa es `GetSaleLinesAsync`, pero se conserva porque corresponde al detalle de líneas/remisión y su eliminación podría afectar paridad futura de detalle.

## 5. Tablas SQLite creadas que no tengan uso

Todas las tablas locales creadas por `LocalDatabase.InitializeAsync` tienen lectura/escritura o uso de auditoría:

| Tabla | Uso |
|---|---|
| `DesktopUsers` | Login local y administración de usuarios. |
| `DesktopPermissions` | Permisos locales. |
| `LocalTarifas` | Tarifas en `OperationsWindow`. |
| `LocalHoteles` | Hoteles en `OperationsWindow`. |
| `LocalTaxistas` | Taxistas y gafetes. |
| `LocalGafetes` | Gafetes/asignación/devolución. |
| `LocalRegistros` | Registro diario y Portal CreateOperation offline. |
| `LocalTransportes` | Transportes y fallback Portal Catalogs. |
| `LocalGuias` | Guías y fallback Portal Catalogs. |
| `LocalGastos` | Gastos. |
| `LocalRelaciones` | Relaciones/dejadas básicas. |
| `LocalProductos` | Productos POS. |
| `LocalVentas` | Ventas POS y Portal CreateOperation offline. |
| `LocalVentaLineas` | Líneas de venta/remisión. |
| `LocalPagos` | Pagos y cortes. |
| `LocalComisiones` | Comisiones y reportes. |
| `LocalCortes` | Cortes. |
| `LocalAuditoria` | Auditoría local y errores. |

No se eliminaron tablas.

## 6. Archivos de prueba, prototipos o código temporal

| Elemento | Estado | Acción |
|---|---|---|
| `ControlTaxi.prueba.db` en `bin/Debug` | Archivo temporal generado por self-test. | Eliminado. |
| Generación futura de `ControlTaxi.prueba.db` en carpeta publicada | Riesgo corregido: `LocalDatabase(forceTestDatabase: true)` usa `%TEMP%/ControlTaxiDesktopSelfTest/<guid>`. | Corregido. |
| Código de self-test | Necesario para validación técnica. | Conservado. |
| Documentación de checklist | Necesaria para validación manual. | Conservada. |

## 7. Riesgos potenciales al instalar en otra computadora

| Riesgo | Estado | Corrección / mitigación |
|---|---|---|
| Base temporal de self-test junto al `.exe` | Corregido. | El self-test usa `%TEMP%`, no `Release/DatosLocal`. |
| Falta de carpeta `DatosLocal` | Controlado. | `LocalDatabase.InitializeAsync` crea la carpeta automáticamente. |
| Falta de `ControlTaxi.db` real | Controlado. | Si no existe, SQLite crea una base local vacía con estructura base; debe importarse/restaurarse para operar con datos reales. |
| Ejecutar desde otra carpeta | Controlado. | `LocalDatabase` busca `DatosLocal/ControlTaxi.db` desde `AppContext.BaseDirectory` y ascendiendo por carpetas. |
| Dependencia de internet/WebView/localhost | No detectada en Desktop. | Las coincidencias `http://` son namespaces XAML/OpenXML, no llamadas de red. |
| DLL nativa SQLite | Controlado. | `Release` contiene `e_sqlite3.dll` y dependencias `Microsoft.Data.Sqlite`/`SQLitePCLRaw`. |
| Impresión física | Requiere revisión manual. | El flujo usa `PrintDialog`; debe probarse en impresora real. |

## Archivos sin uso eliminados

| Archivo | Motivo |
|---|---|
| `ControlTaxiDesktop/bin/Debug/net9.0-windows/DatosLocal/ControlTaxi.prueba.db` | Base temporal de self-test; no es dato real ni recurso necesario para producción. |

No se eliminaron archivos `.cs`, `.xaml`, recursos funcionales, SQLite real, configuración ni documentación necesaria.

## Recursos sin uso

No se encontraron recursos gráficos, XAML o librerías del proyecto Desktop que puedan eliminarse con seguridad sin riesgo funcional.

## Riesgos corregidos

1. Self-test ya no genera base temporal dentro de la carpeta de instalación.
2. Se verificó que `Release/DatosLocal` contiene únicamente `ControlTaxi.db` después del self-test publicado.
3. Se confirmó que no queda `ControlTaxi.prueba.db` bajo `ControlTaxiDesktop/bin`.

## Validación ejecutada

Comandos ejecutados correctamente:

```powershell
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false -o .\Release
.\Release\ControlTaxiDesktop.exe --self-test
```

Resultados:

- Build: correcto, 0 errores.
- Self-test Debug: correcto.
- Validate-parity: correcto.
- Run-pipeline: correcto.
- Publish Windows x64: correcto.
- Self-test del `.exe` publicado: correcto.
- `Release/DatosLocal/ControlTaxi.db`: presente.
- `Release/DatosLocal/ControlTaxi.prueba.db`: no existe.

## Confirmación final

El proyecto Desktop queda limpio a nivel técnico para entrega/mantenimiento, sin evidencia de ventanas huérfanas, botones principales rotos, tablas locales inútiles o dependencias de red. El ejecutable publicado sigue funcionando correctamente después de la limpieza.

La instalación en otra computadora es viable siempre que se incluya la carpeta `Release` completa y, para operación real, `Release/DatosLocal/ControlTaxi.db` con datos importados/reales.
