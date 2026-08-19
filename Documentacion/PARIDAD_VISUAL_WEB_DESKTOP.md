# Paridad visual Web vs Desktop

Fecha: 2026-06-30.

Alcance: revisión y ajuste exclusivamente visual. No se modificó lógica de negocio, cálculos, consultas, SQLite, validaciones funcionales ni archivos del sistema Web.

## Referencia usada

Además del Web antiguo conservado como referencia, se revisó el video entregado por el usuario:

- `C:/Users/reyna/Downloads/Grabación 2026-06-30 120424.mp4`

Se extrajeron capturas locales para comparar pantallas:

- `Documentacion/visual_ref_001.png` a `Documentacion/visual_ref_034.png`

Pantallas observadas en el video:

- Registro diario.
- Registro App.
- Taxistas.
- Gafetes.
- Relación ticket-taxista/dejadas.
- Menú/Login Web previamente mostrado por captura.

## Problema detectado

El Desktop anterior no se veía igual al Web. La diferencia principal era visual:

- Desktop tenía menú lateral oscuro.
- Web usa ventana clásica azul clara.
- Web usa tablero de tarjetas, no sidebar.
- Web usa formularios compactos.
- Web usa tablas con encabezados pequeños y muchas columnas visibles.
- Web usa columna derecha de acciones en varias pantallas.
- Web usa logo HOKA e imagen de taxi.

## Cambios visuales aplicados

Archivos modificados:

- `ControlTaxiDesktop/App.xaml`
- `ControlTaxiDesktop/MainWindow.xaml`
- `ControlTaxiDesktop/MainWindow.xaml.cs`
- `ControlTaxiDesktop/OperationsWindow.xaml`
- `ControlTaxiDesktop/PosWindow.xaml`
- `ControlTaxiDesktop/WebDialogWindow.xaml`
- `ControlTaxiDesktop/WebDialogWindow.xaml.cs`
- `ControlTaxiDesktop/TicketPreviewWindow.xaml`
- `ControlTaxiDesktop/TicketPreviewWindow.xaml.cs`
- `ControlTaxiDesktop/PortalWindow.xaml`
- `ControlTaxiDesktop/ChecklistValidacionFinal.xaml`
- `ControlTaxiDesktop/ControlTaxiDesktop.csproj`

Assets agregados desde el Web:

- `ControlTaxiDesktop/Assets/hoka-logo.png`
- `ControlTaxiDesktop/Assets/taxi.png`

## Comparación por pantalla

| Pantalla Web comparada | Pantalla Desktop equivalente | Ajuste aplicado | Estado |
|---|---|---|---|
| Login Web | `MainWindow` login | Layout dividido con panel navy/teal, logo HOKA, imagen taxi, título `CONTROL TAXI`, tarjeta blanca de login y botón principal. | Más cercano al Web; pendiente ajuste pixel-perfect fino. |
| Dashboard/Menu Web | `MainWindow` panel administrativo | Se reemplazó sidebar por ventana clásica `ADMINISTRACION`, logo HOKA y tablero de tarjetas por módulo. | Más cercano al Web; iconos aún no son SVG exactos. |
| Registro diario Web | `OperationsWindow` / Registros | Marco azul claro tipo ventana clásica, encabezado superior, formulario compacto y tabla blanca compacta. | Más cercano al video; pendiente columna derecha de acciones exacta. |
| Registro App Web | `OperationsWindow` / Registros-catálogo | Formularios compactos, fondo azul claro y paneles blancos. | Más cercano; pendiente layout exacto por secciones Web. |
| Taxistas Web | `OperationsWindow` / Taxistas | Tabla blanca compacta y formulario superior en panel clásico. | Más cercano; acciones tipo link por fila no son idénticas. |
| Gafetes Web | `OperationsWindow` / Gafetes | Panel clásico, tabla compacta y botones pequeños. | Más cercano; pendiente diseño exacto de regreso/impresión lateral. |
| Relaciones/dejadas Web | `OperationsWindow` / Relaciones | Formulario compacto y tabla amplia. | Más cercano; pendiente botones por fila exactamente como Web. |
| Ventas/Pagos/Comisiones/Cortes Web | `PosWindow` | Se cambió marco visual a ventana clásica azul clara, inputs compactos, botones uppercase y tablas compactas. | Más cercano; pendiente reordenar a columna derecha exacta donde aplique. |
| Reportes Web | `PosWindow` / Reportes especializados | Botones y tabla en panel clásico. | Más cercano; pendiente comparación visual archivo por archivo. |
| Usuarios Web | `UserAdminWindow` | Recibe estilos globales de botones, inputs y tablas. | Parcial; falta rediseño específico si se exige igualdad total. |
| Portal Web | `PortalWindow` | Recibe colores y tarjetas globales. | Parcial; falta comparación pixel-perfect por vista Portal. |

## Estilos globales aplicados

En `App.xaml` se compactaron y alinearon controles con el video:

- `Window`: fondo azul/beige Web.
- `Button`: botones pequeños, redondeados, uppercase-friendly.
- `TextBox`: altura reducida tipo formulario Web.
- `PasswordBox`: altura reducida.
- `ComboBox`: altura reducida.
- `DatePicker`: altura reducida.
- `DataGrid`: filas compactas, encabezado pequeño, líneas suaves.
- `TabControl`/`TabItem`: panel azul claro compacto.
- `WebActionSidebar`: columna derecha de acciones tipo Web.
- `WebActionButton`: botón azul/pastel tipo Web.
- `WebActionSaveButton`: botón verde de guardar/cobrar.
- `WebActionGoldButton`: botón dorado para cierre/comisiones.
- `WebActionRedButton`: botón rojo de salida.
- `WebMiniActionButton`: botón compacto por fila.

## Avance adicional aplicado

Después de revisar el video se agregaron estas piezas visuales:

1. `PosWindow` ahora tiene columna derecha de acciones por pestaña:
   - Ventas: `GUARDAR PRODUCTO`, `COBRAR`, `EXCEL`, `PDF`, `MENU`, `SALIR`.
   - Pagos: `REGISTRAR PAGO`, `CSV`, `PDF`, `MENU`, `SALIR`.
   - Comisiones: `COMISIONES`, `PAGAR`, `CSV`, `PDF`, `MENU`, `SALIR`.
   - Cortes: `CALCULAR`, `CIERRE`, `CSV`, `PDF`, `MENU`, `SALIR`.
   - Reportes: reportes Excel/CSV/ticket/impresión en columna derecha.
2. `OperationsWindow` ahora tiene columna derecha de acciones por pestaña:
   - `GUARDAR`, `BUSCAR`, `CSV`, `IMPRIMIR`, `MENU`, `SALIR` según módulo.
3. Se creó `WebDialogWindow` para sustituir avisos genéricos por modal visual estilo Web.
4. Se creó `TicketPreviewWindow` para ver el ticket en formato imprimible antes de imprimir/exportar.

## Evidencia de que no se cambió lógica

No se modificaron:

- `ControlTaxiDesktop/Services/LocalOperationsRepository.cs`
- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
- `ControlTaxiDesktop/Services/LocalPortalRepository.cs`
- `ControlTaxiDesktop/Services/LocalUserRepository.cs`
- `ControlTaxiDesktop/Services/LocalDatabase.cs`
- `ControlTaxiDesktop.Tools/Program.cs`

Los cambios son XAML/estilo/navegación visual. Los nombres de controles y eventos existentes se conservaron.

## Validación técnica

Ejecutado después de los cambios:

- `dotnet build 'CONTROL TAXI.sln' --no-restore`: correcto, 0 errores.
- `dotnet run --project ControlTaxiDesktop/ControlTaxiDesktop.csproj -- --self-test`: correcto.

## Pendientes visuales reales

Para decir “igual tal cual” todavía faltan estos ajustes finos:

1. Sustituir glifos de texto por iconos/SVG exactos del Web.
2. Reordenar cada formulario con los mismos campos, filas y columnas que cada `.cshtml` cuando el flujo WPF tenga controles equivalentes.
3. Replicar botones por fila y enlaces azules de tablas con columnas explícitas donde aplique.
4. Completar modales Web en todas las ventanas restantes que todavía usen `MessageBox`.
5. Comparar capturas lado a lado por módulo y ajustar píxeles/márgenes.

Estado honesto: se corrigió el choque visual principal y el Desktop ya se parece mucho más al Web del video. Aún no está “tal cual” en todas las pantallas internas; para eso hay que continuar pantalla por pantalla con las capturas del video y los `.cshtml`.
