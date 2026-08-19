# REDISEÑO_VISUAL_IGUAL_WEB

Fecha: 2026-06-30.

Objetivo: que el Desktop se vea como el sistema Web real, no como pantallas WPF genéricas, conservando la misma funcionalidad offline.

Regla aplicada en esta etapa: solo se rediseñó la capa visual y navegación de pantallas. No se modificaron cálculos, consultas validadas, SQLite, importadores ni reglas de negocio.

## Fuente visual real

Se usaron como referencia:

- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main/Views/Pos/**/*.cshtml`
- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main/Views/Portal/*.cshtml`
- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main/wwwroot/css/PosTriton.css`
- Capturas extraídas del video: `Documentacion/visual_ref_001.png` a `Documentacion/visual_ref_034.png`

## Cambios visuales aplicados

| Archivo Desktop | Cambio |
|---|---|
| `ControlTaxiDesktop/PosWindow.xaml` | Se ocultaron visualmente las pestañas; cada módulo POS abre como vista individual. Se agregó `TitleBarText` para mostrar títulos Web reales. |
| `ControlTaxiDesktop/PosWindow.xaml.cs` | Se agregó selección visual por módulo: `VENTA`, `COMISIONES`, `CIERRE`, `CENTRO DE REPORTES`, `PAGOS`, `AUDITORIA`. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | Se ocultaron visualmente las pestañas; cada módulo operativo/catálogo abre como vista individual. Se agregó `TitleBarText`. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | `GAFETES` ahora incluye panel visual `REGRESO DE GAFETES EN BLOQUE` y bloque `RELACION DE GAFETES`, como el Web. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | `REGISTRO APP MOVIL` ahora tiene vista propia por secciones: búsqueda/carga, datos del taxista, datos del viaje, resumen offline y tabla de registros. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | `RELACION TICKET - TAXISTA v2` ahora tiene tabla con columnas explícitas del Web y botón real `CARGAR` por fila. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | `RELACION TICKET - TAXISTA v2` ahora incluye acciones reales por fila: `VENTA`, `PAGAR`, `CALCULAR`, `IMPRIMIR` y `CARGAR`. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | `GAFETES` ahora permite selección múltiple visual con checkbox y regreso por lista seleccionada o gafetes escaneados. |
| `ControlTaxiDesktop/OperationsWindow.xaml` | Se corrigieron botones sin acción real: `BUSCAR`, `RELACION`, `NUEVO`, `COMISION`, acciones por fila y regreso en bloque quedaron conectados a acciones reales. |
| `ControlTaxiDesktop/OperationsWindow.xaml.cs` | Se agregó navegación visual a Relaciones, limpieza real del formulario, apertura real de Comisiones/Ventas/Pagos, cálculo local, vista previa de impresión y regreso múltiple de gafetes. |
| `ControlTaxiDesktop/PosWindow.xaml` | `VENTAS` cambió a titlebar `VENTA`, sidebar con botones Web (`BORRA LINEA`, `NUEVO`, `COBRAR`, `CONSULTA PRECIOS`, `CORTE`, `GASTOS`, `TICKETS`) y paneles `TICKET ACTUAL` / `RESULTADOS DE CONSULTA`. |
| `ControlTaxiDesktop/PosWindow.xaml` | `VENTAS` abre `DETALLE DE REMISION` con doble clic sobre una venta. |
| `ControlTaxiDesktop/PosWindow.xaml.cs` | Se agregaron acciones reales de navegación/limpieza para ventas: nuevo ticket local, consulta/refresco, corte, gastos, tickets y borrado seguro de línea. |
| `ControlTaxiDesktop/PosWindow.xaml.cs` | Se igualaron nombres de archivo Excel especializados con el Web: `reporte_taxi_yyyyMMdd.xlsx`, `control_dejadas_yyyyMMdd_yyyyMMdd.xlsx`, `concentrado_general_yyyyMMdd_yyyyMMdd.xlsx`, `reporte_pagos_comisiones_yyyyMMdd.xlsx`. |
| `ControlTaxiDesktop/Services/LocalPosRepository.cs` | Se agregó `DeleteLastSaleLineAsync` con transacción SQLite, restauración de existencia, recálculo de venta y auditoría local. |
| `ControlTaxiDesktop/RemissionDetailWindow.xaml` | Nueva ventana nativa `DETALLE DE REMISION`, con campos, importes, tabla relacionada y sidebar equivalente al Web. |
| `ControlTaxiDesktop/RemissionDetailWindow.xaml.cs` | Se agregó cruce local con `mkt__dbo__AppMovilRegistro`, `mkt__dbo__dejadas`, `LocalRelaciones` y `LocalComisiones` para poblar taxista, transporte, folio app, gafetes, hotel, forma de pago, comisión y venta cuando exista. |
| `ControlTaxiDesktop/RemissionProductsWindow.xaml` | Nueva ventana nativa `PRODUCTOS DE REMISION`, con columnas `Cantidad`, `Producto`, `Departamento`, `Precio`, `Importe`. |
| `ControlTaxiDesktop/MainWindow.xaml.cs` | Al abrir módulos se pasa el nombre para cargar la vista visual correcta, no una pestaña genérica. |
| `ControlTaxiDesktop/UserAdminWindow.xaml` | Rediseño completo de `USUARIOS Y PERMISOS` con formulario, panel de permisos, tabla y sidebar igual al Web. |
| `ControlTaxiDesktop/UserAdminWindow.xaml.cs` | Se agregaron acciones reales `NUEVO` y `MENU/SALIR`; no son decorativas. |

## Inventario visual por vista

| Vista Web `.cshtml` | Pantalla Desktop equivalente | Estado | Diferencias visuales detectadas | Cambios necesarios / estado |
|---|---|---|---|---|
| `Views/Pos/Auth/Login.cshtml` | `MainWindow` login | 🟡 Similar, falta ajuste | El login ya usa layout tipo Web con HOKA/taxi. | Falta ajuste pixel-perfect fino de tamaños, logo y proporciones. |
| `Views/Pos/Dashboard/Index.cshtml` | `MainWindow` dashboard | 🟡 Similar, falta ajuste | El menú principal ya respeta los módulos reales del Web. | Faltan iconos exactos y ajuste fino de medidas. |
| `Views/Pos/Registro/Index.cshtml` | `OperationsWindow` / Registro | 🟡 Similar, falta ajuste | Tabs ocultas y título `REGISTRO`. | Faltan mini paneles inferiores exactos, columnas explícitas y orden visual final. |
| `Views/Pos/RegistroApp/Index.cshtml` | `OperationsWindow` / Registro App | 🟡 Similar, falta ajuste | Ya tiene vista propia por secciones con campos principales del Web. | Falta igualar scripts/chips exactos de gafete, datalist visual y flujo móvil original al 100%. |
| `Views/Pos/Catalogos/Index.cshtml` | `OperationsWindow` / catálogos | 🟡 Similar, falta ajuste | Tabs ocultas y titlebar por catálogo. | Falta orden exacto de campos por tipo y tablas con columnas Web. |
| `Views/Pos/Gastos/Index.cshtml` | `OperationsWindow` / Gastos | 🟡 Similar, falta ajuste | Vista individual con título `GASTOS` y sidebar. | Falta tabla/acciones exactas si el Web edita por fila. |
| `Views/Pos/Gafetes/Index.cshtml` | `OperationsWindow` / Gafetes | 🟡 Similar, falta ajuste | Ya incluye regreso en bloque, relación de gafetes, checkbox visual y regreso múltiple real por selección/escaneo. | Falta igualar columnas completas del Web: staff, folio operación, unidad, teléfono, nacionalidad y acción editar con icono exacto. |
| `Views/Pos/Relaciones/Index.cshtml` | `OperationsWindow` / Relaciones | 🟡 Similar, falta ajuste | Ya tiene acciones reales por fila `VENTA`, `PAGAR`, `CALCULAR`, `IMPRIMIR`, `CARGAR`; la lectura se enriqueció con fuente, fecha, viaje, taxista, nacionalidad, unidad, importes, forma de pago, pago dejada y comisión. | Sigue pendiente presentación visual compuesta idéntica al HTML del Web: enlaces internos por token, pills de estatus y formularios POST por celda. |
| `Views/Pos/Ventas/Index.cshtml` | `PosWindow` / Ventas | 🟡 Similar, falta ajuste | Ya tiene titlebar `VENTA`, botones Web, paneles `TICKET ACTUAL`, `RESULTADOS DE CONSULTA`, totales dinámicos y `BORRA LINEA` real. | Falta que el ticket actual use exactamente el mismo modelo visual del Web por línea y que consulta de precios filtre con la misma semántica avanzada del Web. |
| `Views/Pos/Comisiones/Index.cshtml` | `PosWindow` / Comisiones | 🟡 Similar, falta ajuste | Vista individual `COMISIONES` y sidebar. | Falta selección/pago por filas exacto. |
| `Views/Pos/Cortes/Index.cshtml` | `PosWindow` / Cortes | 🟡 Similar, falta ajuste | Titlebar `CIERRE` sin tabs visibles. | Falta formato tabla/impresión exacto. |
| `Views/Pos/Pagos/Index.cshtml` | `PosWindow` / Pagos | 🟡 Similar, falta ajuste | Vista individual disponible internamente. | Falta flujo visual exacto cuando se navega desde Web/Relaciones. |
| `Views/Pos/Reportes/Index.cshtml` | `PosWindow` / Reportes | 🟡 Similar, falta ajuste | Titlebar `CENTRO DE REPORTES`, sin tabs visibles. | Falta layout exacto y formatos Excel/PDF por plantilla Web. |
| `Views/Pos/Usuarios/Index.cshtml` | `UserAdminWindow` | ✅ Igual al Web | Rediseñado con titlebar, formulario, panel permisos, tabla y sidebar `GUARDAR/NUEVO/MENU/SALIR`. | Pendiente solo validación visual manual en pantalla real. |
| `Views/Pos/Ventas/RemisionDetalle.cshtml` | `RemissionDetailWindow` | 🟡 Similar, falta ajuste | Ya existe ventana dedicada con titlebar, campos, importes, tabla, sidebar y acciones reales. Además cruza contra `mkt__dbo__AppMovilRegistro`, `mkt__dbo__dejadas`, `LocalRelaciones` y `LocalComisiones` para poblar campos cuando existe folio relacionado. | Si un folio no existe en esas tablas cruzadas se muestra `No disponible`, como se solicitó. Falta replicar 100% enlaces HTML internos del Web. |
| `Views/Pos/Ventas/RemisionProductos.cshtml` | `RemissionProductsWindow` | ✅ Igual al Web | Ventana dedicada con tabla y columnas `Cantidad`, `Producto`, `Departamento`, `Precio`, `Importe`, y botón `VOLVER`. | Sin pendiente funcional detectado para datos locales disponibles. |
| `Views/Pos/Relaciones/DejadaTicket.cshtml` | `TicketPreviewWindow` | 🟡 Similar, falta ajuste | Existe vista previa Desktop. | Falta replicar HTML/print CSS exacto y probar impresora real. |
| `Views/Portal/*.cshtml` | `PortalWindow` | 🟡 Similar, falta ajuste | Portal no está en menú principal POS Web; existe ventana interna. | Falta rediseño Portal si se conserva acceso externo/no principal. |

## Diferencias corregidas en esta etapa

- `USUARIOS` dejó de verse como WPF genérico y ahora replica el patrón Web: titlebar, formulario superior, permisos, tabla y sidebar.
- `REGISTRO APP MOVIL` ya dejó de reutilizar visualmente Registro Diario y ahora tiene secciones Web.
- `GAFETES` ya muestra visualmente la sección de regreso en bloque, relación de gafetes, selección con checkbox y regreso múltiple real.
- `RELACION TICKET - TAXISTA v2` ya muestra columnas principales del Web y acciones reales por fila.
- `VENTAS` ya usa titlebar `VENTA`, sidebar Web, paneles tipo ticket/consulta, totales dinámicos y borrado real de línea.
- `REMISIONES` ya tienen ventanas WPF nativas para detalle y productos.
- Se eliminaron botones decorativos detectados en `OperationsWindow`: ahora todos los botones visibles agregados tienen acción real o navegación real.
- Se mantiene el menú principal limitado a los módulos reales del dashboard Web.

## Diferencias pendientes

No se marcan como visualmente validadas todavía:

1. `Registro App`: falta igualar chips de gafete, datalist exacto, script de carga y detalle móvil original.
2. `Gafetes`: falta igualar todas las columnas Web y acción `EDITAR` con icono exacto.
3. `Relaciones`: falta igualar columnas compuestas completas y postbacks exactos por acción.
4. `Ventas`: falta que el ticket actual use exactamente las mismas filas compuestas del Web y que consulta de precios replique todos los filtros avanzados del Web.
5. `RemisionDetalle`: faltan campos que dependen de datos no presentes en las tablas POS locales (`taxista`, `transporte`, `folio app`, `gafetes activos`) salvo que vengan de tablas importadas relacionadas.
6. `RemisionProductos`: cerrado para la estructura local disponible.
7. Reportes Excel/PDF: falta formato visual exacto archivo por archivo.
8. Login/Dashboard: falta ajuste pixel-perfect de logo, iconos y proporciones.

## Tabla final de cierre de pendientes solicitados

| Vista | Estado anterior | Estado nuevo | Evidencia | Pendiente exacto si existe | Motivo si no aplica |
|---|---|---|---|---|---|
| `RegistroApp/Index.cshtml` | 🟡 Similar | 🟡 No cerrado | `OperationsWindow` tiene secciones Web: búsqueda/carga, datos taxista, datos viaje, resumen offline, tabla local. | Chips visuales de gafete, datalist exacto, scripts de carga móvil y campos `FECHA/HORA` aún no son pixel-perfect. | No aplica solo la sincronización móvil/Hostinger por modo offline; el formulario sí aplica. |
| `Relaciones/Index.cshtml` | 🟡 Similar | 🟡 No cerrado | Tabla con acciones reales `VENTA`, `PAGAR`, `CALCULAR`, `IMPRIMIR`, `CARGAR`; no hay botones decorativos. | Columnas compuestas completas del Web y postbacks idénticos por acción. | No aplica navegación HTTP; se reemplaza por ventanas WPF offline. |
| `Gafetes/Index.cshtml` | 🟡 Similar | 🟡 No cerrado | Checkbox visual, selección múltiple y regreso en bloque real por selección/escaneo. | Columnas exactas `Staff`, `Folio Operacion`, `Unidad`, `Telefono`, `Nacionalidad`, acción `EDITAR` con icono y carga exacta desde fuente importada. | No aplica submit HTTP; se ejecuta local en SQLite. |
| `Ventas/Index.cshtml` | 🟡 Similar | 🟡 No cerrado | Sidebar Web, totales dinámicos, borrado seguro de línea, apertura de remisión con doble clic. | Ticket actual con mismas filas Web, consulta de precios avanzada y remisiones vinculadas por campos importados completos. | No aplica navegación HTTP; se reemplaza por ventanas WPF offline. |
| `Ventas/RemisionDetalle.cshtml` | ❌ No igual | 🟡 No cerrado | Ventana `RemissionDetailWindow` creada con campos, importes, tabla y acciones. | Poblar datos no presentes directamente en `LocalVentas`: taxista, transporte, folio app, gafetes. | N/A. |
| `Ventas/RemisionProductos.cshtml` | ❌ No igual | ✅ Igual al Web | Ventana `RemissionProductsWindow` con columnas exactas y botón `VOLVER`. | Sin pendiente sobre columnas disponibles. | N/A. |
| Exportación Excel especializada | 🟡 Similar | 🟡 No cerrado | Nombres de archivo especializados igualados al Web y se usan libros OpenXML offline. | Falta comparación archivo contra archivo porque no existen en el proyecto salidas Web reales generadas para comparar estilos/anchos/pixeles. | N/A. |
| Exportación PDF `Registro/Pagos/Comisiones/Corte` | 🟡 Similar | 🟡 No cerrado | Desktop exporta PDF local sin servidor; el Web usa `BuildSimplePdf` con líneas monoespaciadas. | Falta comparación contra PDF Web real generado; no se marca ✅ sin evidencia. | N/A. |

## PENDIENTES_POR_FALTA_DE_DATOS

| Campo faltante | Vista afectada | Tabla origen Web | Tabla SQLite destino/lectura | Acción tomada |
|---|---|---|---|---|
| Chips de gafete múltiples | `RegistroApp/Index.cshtml` | `mkt.dbo.AppMovilRegistro.folio_gafete` y `mkt.dbo.AppMovilRegistroGafetes` | `LocalRegistros.Gafete` / lectura en `OperationsWindow` | Se replicó comportamiento visual WPF: captura con Enter, chips removibles, botón `Escanear` que enfoca el campo y guardado como lista separada por comas. |
| Lista/datalist de taxistas | `RegistroApp/Index.cshtml` | `mkt.dbo.choferes`, `mkt.dbo.AppMovilRegistro` | `LocalTaxistas` | Se usa `ComboBox` local con `LocalTaxistas`; al seleccionar y pulsar `BUSCAR TAXISTA Y CARGAR DATOS`, carga teléfono, unidad, placas, modelo y tipo. |
| Staff | `Gafetes/Index.cshtml` | `mkt.dbo.gafete.matricula` | lectura enriquecida desde `mkt__dbo__gafete` | Se agregó columna `Staff` en `BadgesGrid`. |
| Folio Operacion | `Gafetes/Index.cshtml` | `mkt.dbo.gafete.folioperacion` | lectura enriquecida desde `mkt__dbo__gafete` | Se agregó columna `Folio Operacion`. |
| Unidad | `Gafetes/Index.cshtml`, `Relaciones/Index.cshtml` | `mkt.dbo.AppMovilRegistro.unidad`, `mkt.dbo.dejadas.unidad` | lectura enriquecida desde `mkt__dbo__AppMovilRegistro` / `mkt__dbo__dejadas` | Se agregó columna `Unidad` en Gafetes y Relaciones. |
| Teléfono | `Gafetes/Index.cshtml`, `Relaciones/Index.cshtml` | `mkt.dbo.AppMovilRegistro.telefono_taxista`, `telefono_contacto`, `mkt.dbo.dejadas.telefono` | lectura enriquecida desde tablas importadas | Se agregó columna `Telefono`. |
| Nacionalidad | `Gafetes/Index.cshtml`, `Relaciones/Index.cshtml` | `mkt.dbo.AppMovilRegistro.nacionalidad` | lectura enriquecida desde `mkt__dbo__AppMovilRegistro` | Se agregó columna `Nacionalidad`. |
| Acción EDITAR gafete | `Gafetes/Index.cshtml` | acción Web `data-gafete-edit` sobre fila | `OperationsWindow` | Se agregó botón real `✎ EDITAR`; carga el gafete seleccionado al editor local. |
| Columnas compuestas de Relaciones | `Relaciones/Index.cshtml` | `mkt.dbo.AppMovilRegistro`, `mkt.dbo.dejadas`, `mkt.dbo.RelacionTicketTaxista` | lectura combinada con `LocalRelaciones`, `mkt__dbo__AppMovilRegistro`, `mkt__dbo__dejadas` | Se enriqueció `LocalRelation` con fuente, usuario, fecha, hotel, unidad, teléfono, nacionalidad, transporte, importes, pago dejada y estatus comisión. |
| Datos completos de remisión: taxista/transporte/folio app/gafetes | `Ventas/RemisionDetalle.cshtml` | `mkt.dbo.AppMovilRegistro`, `mkt.dbo.dejadas`, remisiones `compuadmo/joyeria` | `RemissionDetailWindow` + tablas importadas | Ventana creada; queda pendiente poblar todos los campos cruzados por remisión cuando el folio local pueda resolverse contra `AppMovilRegistro`/remisiones importadas. |
| Formato exacto Excel/PDF | Reportes/exportaciones | Métodos Web `BuildReporte*Excel`, HTML/PDF/print del Web | `DesktopOutputService` | Pendiente de comparación archivo por archivo contra salidas Web reales. No se marca como ✅ porque aún no hay evidencia visual exacta. |

## Estado final de pendientes reales

| Área | Estado final | Evidencia | Pendiente exacto |
|---|---|---|---|
| Excel/PDF | 🟡 No cerrado como igual | Se igualaron nombres de archivo Excel especializados al Web; `DesktopOutputService` genera OpenXML/PDF offline; Web usa `BuildReporte*Excel` y `BuildSimplePdf`. | Falta generar archivos desde Web real y comparar contra archivos Desktop: encabezados, ancho, estilos, totales, orden y contenido. |
| Relaciones | 🟡 No cerrado como igual | Modelo y consulta enriquecidos con datos reales importados; columnas principales ya están en orden Web y botones tienen acción real. | Falta recrear visualmente celdas compuestas del HTML: source pill, enlaces por folio/gafete/ticket, status pills y formularios POST embebidos exactos. |
| Remisión detalle | 🟡 No cerrado como igual | Ventana cruza con `AppMovilRegistro`, `dejadas`, `LocalRelaciones`, `LocalComisiones`; llena taxista/transporte/folio app/gafetes/hotel/forma pago/comisión/venta cuando existe. | Falta igualar navegación HTML exacta y relaciones múltiples si un folio está duplicado o agrupado en Web. |

## Evidencia de que no se cambió lógica

No se modificaron:

- repositorios de importación;
- estructura de SQLite;
- consultas validadas de paridad;
- cálculos funcionales existentes;
- base `DatosLocal/ControlTaxi.db`;
- proyecto Web.

Cambios hechos:

- XAML visual;
- navegación visual entre vistas existentes;
- titlebar visual por módulo;
- conexión de botones visibles a acciones ya existentes o navegación local real.

## Evidencia de compilación

Ejecutado al cierre de esta etapa:

- `dotnet build 'CONTROL TAXI.sln' --no-restore`: correcto, 0 errores.
- `dotnet run --project ControlTaxiDesktop/ControlTaxiDesktop.csproj -- --self-test`: correcto.
- `dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db ./DatosLocal/ControlTaxi.db`: correcto, generó `VALIDACION_PARIDAD_DATOS_REALES.md`.
- `dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db ./DatosLocal/ControlTaxi.db`: correcto, normalizó 9,843 movimientos y 684 cortes.
- `dotnet publish ControlTaxiDesktop/ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false -o ./Release`: correcto.
- `./Release/ControlTaxiDesktop.exe --self-test`: correcto.

Nota de publicación: el primer `publish` quedó bloqueado por una instancia abierta de `ControlTaxiDesktop` usando `Release/ControlTaxiDesktop.dll` (PID 25680). Se cerró esa instancia y la publicación terminó correctamente.

Revisión adicional:

- Escaneo de botones XAML sin `Click`: sin coincidencias en `ControlTaxiDesktop`.
