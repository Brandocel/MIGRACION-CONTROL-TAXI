# Parche del cuadre en el API de Hostinger — procedimiento seguro

Fecha: 2026-09-09

Este parche agrega tres rutas al API PHP de Hostinger para que Hoka Solutions pueda mostrar y
descargar el cuadre de Plaza 28 (las 5 hojas del Excel del escritorio). El API que se toca es el
mismo que ya alimenta el reporte de dejadas de Hoka y la app móvil, así que el riesgo real está
en romper algo que hoy funciona. Todo el procedimiento de abajo está diseñado alrededor de eso.

## Qué agrega

| Ruta | Método | Para qué |
|---|---|---|
| `/sync/push-cuadre` | POST | El escritorio sube el cuadre ya calculado. Protegida por el `X-Sync-Token` que `route_sync()` ya exige. |
| `/api/pos/cuadre` | GET | Hoka lee el cuadre de un rango y sucursal. |
| `/api/pos/cuadre/disponibles` | GET | Diagnóstico: qué rangos hay guardados, sin entrar a phpMyAdmin. |

Y una tabla nueva, `mkt2_cuadre_snapshots`, creada con `CREATE TABLE IF NOT EXISTS` desde
`ensure_cuadre_schema()`, que solo se invoca dentro de esas tres rutas.

## Por qué el cuadre no se calcula en PHP

Las reglas de comisión viven en C# (`LocalPosRepository` / `DesktopOutputService`) y ya costaron
tres sesiones de correcciones: guías al 8 % por el singular/plural, venta duplicada cuando un
taxista llega dos veces el mismo día, y códigos cortos del POS robándole la regla a unidades
específicas. Reescribir eso en PHP crea una segunda fuente de verdad y garantiza que Hoka y el
Excel del escritorio terminen dando números distintos — que es exactamente el problema que se
persiguió el 28/08. Por eso el escritorio calcula y empuja; el API solo guarda y devuelve.

## Por qué el parche es seguro

- **Aditivo puro.** El diff contra el archivo del 04/09 da 165 líneas agregadas y **cero
  eliminadas**. Ninguna ruta existente cambia de comportamiento.
- **No toca el ruteo.** `route()` ya manda a `route_pos_get()` cualquier GET que empiece con
  `/api/pos/`, así que `/api/pos/cuadre` entra solo. No hay que modificar el filtro de la
  línea ~718.
- **No toca tablas existentes.** Nada de `mkt2_trip_records`, `mkt2_catalog_taxis`,
  `mkt2_pos_operations`, `mkt2_gafetes`.
- **Falla aislada.** `ensure_cuadre_schema()` no corre en el arranque. Si la creación de tabla
  fallara, falla el cuadre y nada más.
- **No se edita a mano.** `aplicar-parche-cuadre.ps1` inserta los bloques por anclaje y aborta
  sin escribir nada si algo no calza. Verifica además que no se haya perdido ninguna ruta y que
  el balance de llaves no cambie.

## Procedimiento

### 1. Bajar el archivo que está en el servidor AHORITA

No usar la copia local `index-SERVIDOR-con-camiones-2026-09-04.php`. Según el contexto del
04/09 ya se subió un parche al API después de esa copia, y sobrescribir con la versión vieja
borraría ese trabajo. Bajar `index.php` por hPanel (Administrador de archivos) o FTP y
guardarlo, por ejemplo, como `index-servidor-20260909.php`.

Comparar contra la copia local para saber qué tanto cambió:

```bash
diff "index-SERVIDOR-con-camiones-2026-09-04.php" "index-servidor-20260909.php"
```

### 2. Foto del API antes de tocar nada

```bash
powershell -ExecutionPolicy Bypass -File "Documentacion\verificar-api-cuadre.ps1" -Salida antes.json
```

Las rutas del cuadre van a dar 404. Es lo esperado: todavía no existen.

### 3. Generar el archivo parchado

```bash
powershell -ExecutionPolicy Bypass -File "Documentacion\aplicar-parche-cuadre.ps1" -IndexPath "index-servidor-20260909.php"
```

Deja dos archivos: `index-servidor-20260909.php.backup-AAAAMMDD-HHMMSS` (respaldo intacto) y
`index-servidor-20260909parchado.php` (el que se sube). El script no sube nada.

### 4. Respaldo en el servidor

En hPanel, antes de subir: renombrar el `index.php` del servidor a
`index.php.backup-20260909`. Eso es lo que se restaura si algo sale mal — un renombrado, no
una restauración de respaldo de Hostinger que tarda.

### 5. Subir

Subir el archivo parchado como `index.php`.

### 6. Comprobar de inmediato

```bash
powershell -ExecutionPolicy Bypass -File "Documentacion\verificar-api-cuadre.ps1" -Salida despues.json
powershell -ExecutionPolicy Bypass -File "Documentacion\verificar-api-cuadre.ps1" -Comparar antes.json despues.json
```

Criterio de aceptación:

- Toda ruta que daba 200 antes sigue dando 200. Si alguna cambió de código HTTP, el script
  sale con error y hay que restaurar.
- Las dos rutas nuevas del cuadre pasan de 404 a 200.
- Los conteos de elementos pueden variar: son datos vivos que cambian durante el día. Eso sale
  en amarillo y no es falla.

Revisar también en Hoka que el reporte de dejadas siga cargando, y en la app móvil que el
listado de taxistas siga saliendo.

### 7. Si algo se rompió

Renombrar `index.php.backup-20260909` de vuelta a `index.php` en hPanel. El API vuelve al
estado anterior en segundos. La tabla `mkt2_cuadre_snapshots` puede quedarse: no la lee nadie
más y no estorba.

## Contrato de datos

`POST /sync/push-cuadre` recibe, y `GET /api/pos/cuadre` devuelve:

```json
{
  "branchCode": "plaza28",
  "fechaInicio": "2026-09-09",
  "fechaFin": "2026-09-09",
  "generatedAt": "2026-09-09T10:01:00-06:00",
  "disponible": true,
  "resumen":    [{"concepto":"TAXIS VERDES","pax":2,"adultos":2,"jovenes":0,"menores":0,"entraron":2,"salieron":0,"unidades":1,"dejada":450.0,"comision":0,"venta":0,"gastos":450.0,"ticketPromedio":0,"porcentajeGasto":0}],
  "camiones":   [{"concepto":"AUTOCAR","pax":17,"entraron":13,"salieron":4,"unidades":5,"dejada":210.0}],
  "dejadas":    [{"fecha":"2026-09-09","folio":"5431","hora":"09:33:22","nombre":"...","vendedor":"...","unidad":"VAN VERDE","numero":"8331","origen":"RIU CANCUN","sitioHotel":"Tienda Plaza 28","adulto":2,"joven":0,"nino":0,"pax":2,"importeDejada":450.0,"telefono":"...","venta":0,"estatusDejada":"pagado","fechaPagoDejada":"2026-09-09","comision":0,"pagoComision":0}],
  "hoteles":    [{"hotel":"RIU CANCUN","pax":2,"dejada":450.0,"venta":0,"comision":0,"pago":0}],
  "comisiones": [{"folio":"","fecha":"","taxista":"","venta":0,"comision":0,"pagado":0,"saldo":0,"estatus":""}],
  "corteFinal": [{"fecha":"2026-09-09","efectivo":0,"tarjeta":0,"pagos":0,"gastos":0,"esperado":0,"contado":0,"diferencia":0,"estatus":""}]
}
```

`sucursal` acepta `plaza28` (por omisión) o `cascoviejo`, porque el escritorio genera dos
cuadres distintos (`ExportCuadreWorkbookAsync` y `ExportCascoCuadreWorkbookAsync`).

Cuando no hay snapshot para el rango pedido, la respuesta viene con `"disponible": false` y las
seis listas vacías, en vez de un error. Así Hoka pinta "sin datos" y no truena.
