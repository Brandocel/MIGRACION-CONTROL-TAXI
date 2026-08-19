# Manual de usuario Desktop

Fecha: 2026-06-29.

## Inicio

1. Abrir `ControlTaxiDesktop.exe`.
2. Escribir usuario y contrasena.
3. Presionar `Entrar`.

El menu muestra solo los modulos permitidos para el usuario.

## Modulos principales

| Modulo | Uso |
|---|---|
| Registro diario | Alta y consulta de registros locales. |
| Ventas | Productos, ventas y cobros locales. |
| Pagos | Registro y consulta de pagos. |
| Comisiones | Recalculo, consulta y abonos. |
| Cortes | Calculo y cierre de cortes. |
| Gafetes | Alta, asignacion, devolucion y consulta. |
| Reportes | Excel/PDF de reportes especializados. |
| Usuarios | Administracion de usuarios y permisos. |
| Auditoria | Consulta de eventos y errores registrados. |

## Exportaciones

En los modulos con boton de exportacion:

1. Seleccionar rango de fechas si aplica.
2. Presionar el boton de Excel/PDF.
3. Elegir carpeta y nombre de archivo.
4. Guardar.

## Ticket de dejada

1. Abrir `Reportes especializados`.
2. Escribir folio de dejada/ticket.
3. Presionar `Vista ticket` para guardar texto.
4. Presionar `Imprimir ticket` para enviar a impresora.

La impresion final depende de la impresora instalada en Windows.

## Recomendaciones

- No cerrar la aplicacion mientras se guarda o exporta.
- Respaldar diariamente `DatosLocal\ControlTaxi.db`.
- No editar la base SQLite manualmente.
- Si aparece un error, revisar el modulo de Auditoria o reportarlo al encargado tecnico.

