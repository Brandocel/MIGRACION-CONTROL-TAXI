# Contexto de sesión — 2026-09-09

## Qué se hizo

El cuadre de Plaza 28, que hasta hoy sólo existía en el escritorio de la tienda, ya se ve y se
descarga desde Hoka Solutions. Quedó completo de punta a punta: el escritorio lo calcula, el
sincronizador lo publica solo cada 5 minutos, y Hoka lo muestra en cinco pestañas con botón de
Excel.

## La decisión de fondo

Los dos sistemas viven en servidores distintos: el cuadre se calcula contra el SQL Server que
está dentro de la tienda, y Hoka corre en `SRVHOKA`. Se descartó abrir un túnel entre ambos —
dejaría el reporte de Hoka dependiendo de que la red de la tienda esté arriba, y obligaría a
portar a MVC toda la aritmética que hoy vive en WPF.

En su lugar se reusó el puente que ya existía: la API PHP de Hostinger, por la que ya viajaban
las dejadas y los camiones. El escritorio calcula el cuadre con sus propias reglas y publica el
resultado ya cuadrado; el API sólo lo guarda y lo devuelve; Hoka sólo lo lee.

**Las reglas de comisión no se duplicaron en ningún lado.** No se reescribieron en PHP ni en
PowerShell. Las que se corrigieron entre el 28/08 y el 01/09 (guías al 8 % por el
singular/plural, venta duplicada cuando un taxista llega dos veces el mismo día, códigos cortos
del POS robándole la regla a unidades específicas) siguen viviendo únicamente en C#. Si Hoka las
recalculara por su cuenta, tarde o temprano los dos reportes darían cifras distintas — que es
justo el problema que se persiguió el 28/08.

## API de Hostinger — parche aplicado

Se agregaron tres rutas al `index.php`, de forma **aditiva**: 166 líneas agregadas, cero
eliminadas.

| Ruta | Método | Para qué |
|---|---|---|
| `/sync/push-cuadre` | POST | El escritorio sube el cuadre ya calculado. Protegida por el `X-Sync-Token` que `route_sync()` ya exigía. |
| `/api/pos/cuadre` | GET | Hoka lee el cuadre de un rango y sucursal (`plaza28` o `cascoviejo`). |
| `/api/pos/cuadre/disponibles` | GET | Diagnóstico: qué rangos hay guardados, sin entrar a phpMyAdmin. |

Tabla nueva `mkt2_cuadre_snapshots`, con el cuadre completo de un rango guardado como un solo
JSON. Se prefirió un payload sobre cinco tablas normalizadas porque el escritorio ya produce la
estructura exacta y Hoka la consume tal cual: menos superficie que romper, y cambiar una columna
del Excel no obliga a migrar la base. `ensure_cuadre_schema()` se invoca únicamente dentro de las
rutas nuevas, nunca en el arranque, para que un error ahí no tumbe el resto del API.

Cuando no hay cuadre publicado para el rango, la respuesta viene con `"disponible": false` y las
seis listas vacías, en lugar de un error, para que la pantalla muestre "sin datos" y no truene.

Archivos en `Documentacion/`: `PARCHE-CUADRE-API-2026-09-09.php` (los tres bloques),
`aplicar-parche-cuadre.ps1` (inserta por anclaje, aborta si algo no calza),
`verificar-api-cuadre.ps1` (prueba de humo antes/después) y
`PARCHE-CUADRE-API-2026-09-09-INSTRUCCIONES.md`.

**Detalle que justificó el parcheador automático:** la cadena `'Ruta no encontrada'` aparece dos
veces en el `index.php` (fin de `route()` y fin de `route_pos_get()`). Pegar en la equivocada
rompe el ruteo completo. El script ancla por posición relativa a `function route_pos_get`.

## Escritorio — el cuadre se publica solo

Archivos nuevos en `ControlTaxiDesktop`:

- `Models/CuadrePayloadModels.cs` — las seis secciones del cuadre como records.
- `Services/DesktopOutputService.Cuadre.cs` — `BuildCuadrePayload`. Es **clase parcial** del
  mismo `DesktopOutputService`, a propósito: así llama a los mismos privados que arman el Excel
  (`BuildCategorySummaries`, `MergeCamionesIntoCategorySummaries`,
  `ApplyPlaza28HistoricalCuadreReconciliation` y las correcciones de despliegue) en vez de
  reimplementar la aritmética.
- `Services/CuadrePushService.cs` — el `POST`, con el token leído de
  `SyncTaxi_Plaza28/sync.plaza28.config.json`, para que viva en un solo lugar.
- `Services/CuadreSnapshotService.cs` — arma el cuadre **sin abrir ninguna ventana**, con las
  mismas cuatro consultas que hace el botón.

En `App.xaml.cs` se agregó la bandera `--push-cuadre`, siguiendo el patrón que ya usaban
`--limpiar-catalogo` y `--diag-sql`. Va como bandera del ejecutable y no como programa aparte
porque `ControlTaxiDesktop.exe` ya está instalado en todas las máquinas; un binario nuevo
obligaría a rehacer el paquete y el instalador. Escribe bitácora en `Logs\push-cuadre.txt` y
devuelve código de salida 0 / 1.

`SyncTaxi_Plaza28/sync-plaza28-loop.ps1` la invoca al final de cada ciclo, con reloj propio: el
ciclo normal corre cada 15 segundos, pero el cuadre se publica cada 5 minutos
(`CuadrePushIntervalSeconds`). Armar el cuadre relee relaciones, comisiones, cortes y camiones
del día entero; hacerlo cada 15 segundos castigaría a SQL Server sin ganar nada. Publica de
inmediato en el primer ciclo, va en su propio `try` para que un fallo de red no marque el ciclo
de sincronización como fallido, y corta a los 180 segundos si se cuelga.

**Por qué el sincronizador y no el botón:** así lo pidió el usuario. Si la publicación depende de
que alguien exporte el Excel, Hoka se queda con el dato del día anterior y nadie se entera. El
botón `EXCEL CUADRE` también publica, como conveniencia cuando alguien sí exporta, pero ya no es
el único camino.

Paquete instalado en Plaza 28: `PLAZA28-DESKTOP-20260909-2.zip`.

## Hoka Solutions — pantalla nueva

Archivos nuevos: `Models/CuadreApiModels.cs`, `Models/CuadreReporteViewModel.cs`,
`Service/CuadreApiService.cs`, `Service/CuadreReporteQueryService.cs`,
`Service/CuadreExcelBuilder.cs` (ClosedXML) y `Views/AdminPages/ReporteCuadreAdmin.cshtml`.
Acciones `ReporteCuadreAdmin` y `DescargarCuadreExcel` en `AdminPagesController`. Entrada de menú
**CUADRE PLAZA 28** junto a REPORTE DE TAXIS, con el mismo permiso (`tienePermiso33ReporteTaxis`):
es el mismo dato, visto por concepto en lugar de renglón por renglón.

El botón de Excel **vuelve a consultar la API** en lugar de guardar el modelo en sesión, así el
archivo siempre trae lo último que publicó la tienda y no una copia vieja de la pantalla.

Claves nuevas en `Web.config`: `CuadreApi.BaseUrl`, `CuadreApi.Sucursal`, `CuadreApi.TimeoutSeconds`.

## Publicación de Hoka — lo que costó trabajo averiguar

**El sitio correcto es `TritonWeb`, en `F:\TritonWeb`, puerto 83.** No es `HokaIngresos`, que era
la suposición inicial: ése corre un `hoka.dll` de agosto de 2024 y su `Web.config` no tiene
`TaxisApi.BaseUrl`. La forma de identificarlo fue recorrer los sitios de IIS buscando cuál tiene
esa clave, porque es el que corre el reporte de dejadas.

Se creó `Publicar_HokaCuadre.ps1`, con la misma forma que el `Publicar_HokaCXP.ps1` que ya usaban:
comprobaciones, respaldo en `C:\Respaldos\HokaCuadre_<fecha>`, `app_offline.htm`, copia,
verificación y comando de rollback impreso al final.

Dos decisiones de seguridad que vale la pena recordar:

1. **El `Web.config` se parcha con XML DOM, no se reemplaza.** El del servidor tiene cosas que el
   del repo no. El script agrega sólo las claves que faltan.
2. **El menú se inserta sobre el archivo del servidor, no se copia encima.** El
   `_LayoutAdmin.cshtml` publicado y el del repo difieren en 78 bytes: alguien editó uno de los
   dos por su cuenta. El script ancla al bloque de `ReporteTaxisAdmin` e inserta el renglón
   después, y se salta el paso si ya está.

Las librerías de Excel van en `bin-opcional\` y sólo se copian si al sitio le faltan: si ya están,
se respeta su versión porque otros reportes de Hoka las usan. En la publicación real ya estaban.

Publicado el 09/09/2026 13:30, sin avisos. Respaldo en `C:\Respaldos\HokaCuadre_20260909_133038`.
El `hoka.dll` anterior era del 12/08/2026 12:55.

## Un susto que resultó ser de zona horaria

Antes de publicar pareció que había cinco archivos de Tesorería modificados **después** del DLL
desplegado, o sea trabajo sin publicar que se iría montado en el mismo binario. Comparando
tamaños —que no dependen del reloj— resultó que tres de los cuatro archivos de vista son byte por
byte idénticos a los del servidor, y que las fechas del servidor salen exactamente una hora antes
que las del repo. Era desfase de zona horaria entre las dos máquinas. Tesorería ya estaba
publicada.

**Lección para la próxima:** en este par de máquinas las fechas de archivo no son comparables
directamente; hay que comparar tamaños o hashes.

## Pendientes

- **Casco Viejo no está cubierto.** `/casco-api/` es un `index.php` aparte y no lleva el parche.
  Si alguien exporta el cuadre desde CV, el push falla con aviso. Se arregla con el mismo
  procedimiento y los mismos scripts.
- **El repo está atrasado en `Views/TesoreriaCaja/Index.cshtml`**: el servidor tiene 156 bytes
  que el repo no tiene, editados directo en `F:\TritonWeb` el 12/08 a las 14:46. Conviene bajar
  esa versión al repo antes de que se pierda.
- Faltaba instalar `PLAZA28-DESKTOP-20260909-2.zip` en las otras dos máquinas de la tienda, las
  que traían pendiente el `-3` del 04/09.
- Las cinco pruebas en pantalla de Hoka (menú, tarjetas, las cinco pestañas, descarga del Excel,
  y que el REPORTE DE TAXIS siga igual) quedaron por hacer al cierre de la sesión.
