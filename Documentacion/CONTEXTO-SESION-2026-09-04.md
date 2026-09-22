# Contexto de trabajo — 4 de septiembre de 2026

Sesión dedicada al **sincronizador**: se revisó por qué reportaban que "no baja la información",
se le agregaron los camiones de la segunda cuenta de Hostinger, y se taparon tres huecos que
hacían que una falla pasara desapercibida.

Continuación de `CONTEXTO-SESION-2026-09-01.md`.

---

## 1. El reporte de "no baja la información": qué se encontró

**Los datos sí están bajando.** Verificado en vivo el 04/09 contra la máquina de Plaza 28:

| | |
|---|---|
| Hostinger, registros del día | 28 (folios 5183 a 5210) |
| SQL Server `dbo.AppMovilRegistro`, mismo día | 28 |
| Faltantes | **0** |
| Pendientes sin marcar en Hostinger | 0 |

O sea que la app móvil sube bien, la nube tiene los datos, y el sincronizador los está copiando.
El problema es **intermitente**, y lo que lo vuelve difícil de agarrar es que hasta hoy **no
había forma de saber cuándo falla**. Eso es lo que se arregló.

### 1.1 Lo que estaba realmente frágil

- El sincronizador vive de un proceso `powershell.exe` que corre en loop. En la máquina de
  Plaza 28 el PID 6300 llevaba corriendo desde el **21 de agosto**. Si ese proceso se muere sin
  que la máquina se reinicie, la tarea programada — que sólo dispara `AtStartup` — no lo vuelve
  a levantar, y nadie se entera.
- El loop leía `MaxRunSeconds` de su configuración y **nunca lo usaba**. Un ciclo colgado en red
  o en SQL se quedaba esperando para siempre.
- El archivo de estado decía `LastHttp: 200, LastError: ""` aunque el ciclo hubiera tirado avisos.
  Los `Write-Warning` del script hijo ni siquiera llegaban al log, porque se capturaba `2>&1`
  (sólo errores) en vez de `*>&1` (todos los flujos).

### 1.2 Ojo con el nombre de la tarea

La tarea programada se llama **`ControlTaxi Plaza28 Sync`** (así, con espacios). En esta sesión
se perdió tiempo buscándola con un nombre inventado y se concluyó de más que no existía.
Para consultarla:

```powershell
Get-ScheduledTask -TaskName 'ControlTaxi Plaza28 Sync' | Select-Object TaskName, State
```

El resultado `267009` de la tarea es `0x41301` = "corriendo", no un error.

---

## 2. Herramienta nueva: diagnóstico del sincronizador

`ControlTaxiDesktop\SyncTaxi_Plaza28\revisar-sincronizador-plaza28.ps1`, con su
`REVISAR-SINCRONIZADOR-PLAZA28.cmd`. **Sólo lee**, no escribe en Hostinger ni en SQL Server.

Se corre **en la máquina de la tienda, en el momento en que reporten la falla** — es cuando
sirve, porque atrapa el problema en el acto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\revisar-sincronizador-plaza28.ps1 -Dias 2
```

Reporta: si la tarea existe y en qué estado, si el proceso del loop vive y desde cuándo, hace
cuánto fue el último ciclo, cuántos registros tiene Hostinger contra cuántos SQL Server, y
**lista folio por folio los que faltan** — marcando cuáles están en `AppMovilFoliosBloqueados`
(que el sync omite a propósito) y cuántos bajaron sin dejada.

### 2.1 Detalle de PowerShell que costó una vuelta

`Invoke-RestMethod` a veces entrega el arreglo JSON como **un solo objeto**, y entonces `@(...)`
lo envuelve en lugar de expandirlo: el conteo sale `1` en vez de `28` y todos los folios parecen
faltantes. El script ya lo corrige:

```powershell
if ($remotos.Count -eq 1 -and $remotos[0] -is [array]) { $remotos = @($remotos[0]) }
```

---

## 3. Camiones: ahora bajan por el sincronizador

**Lo que se quería:** que los camiones (AUTOCAR / MAYA CARIBE / TURICUN) lleguen a SQL Server
local igual que los taxis, y que el cuadre lea de local. Cierra el pendiente 3 de la sesión del
28/08 en su parte de datos.

Los camiones viven en la **segunda cuenta de Hostinger**: `u679771392_choferes`, tablas
`choferes` (800 filas), `registros` (14,931), `control`, `tabla_control`, `tabla_control_pax`.
La copia en SQL Server estaba en **13,168 filas, última fecha 18/08/2026** — 17 días atrás.

### 3.1 API: dos endpoints nuevos

`/api/pos/camiones` ya existía y funciona, pero devuelve el **resumen ya sumado**; para
sincronizar hacen falta los renglones. Se agregaron, dentro de `route_pos_get`:

| Endpoint | Devuelve |
|---|---|
| `GET /api/pos/camiones/registros?desde=&hasta=` | Un renglón por registro, con la empresa del chofer. Tope 20,000. |
| `GET /api/pos/camiones/choferes` | El catálogo completo. |

**Ya están arriba en producción** (04/09). Se parchearon sobre el archivo del servidor, no se
reemplazó con la copia local — siguen divergidos: el local tiene monto sugerido en el catálogo,
el arreglo de `driverKey` de Plaza 28 y el renombrado de hoteles, que el servidor no. Respaldo del
anterior en `index_bak_2026-09-04.php`; copia de lo que quedó arriba en
`Documentacion/index-SERVIDOR-con-camiones-2026-09-04.php`. El bloque suelto y los pasos, en
`PARCHE-API-camiones-para-sincronizador.md`.

Verificado tras subirlo: `/health` ok, 44 registros y 800 choferes del 04/09, y las rutas que ya
usaban la app y el escritorio (`/api/taxis/options`, `/api/pos/transportes`,
`/api/taxis/registros`) respondiendo igual que antes.

### 3.2 Sincronizador: `Sync-CamionesFromHostinger`

En `SyncTaxi_Plaza28\sync-sqlserver-hostinger-bidirectional.ps1`:

- Baja los últimos **3 días** a `dbo.registroscamiones`, por `Id_registros`: actualiza si existe,
  inserta si no.
- **No toca `pagos`, `fechadepago` ni `Id_tipo`** — son columnas del POS local, ahí viven los
  pagos capturados en la tienda.
- Registros cada **5 minutos**, catálogo de choferes cada **hora** (marca de tiempo en
  `camiones-ultima-corrida.txt`, junto al script). El loop llama al script cada 15 segundos y no
  tiene caso escribir cientos de filas por vuelta.
- Crea, si faltan, índice único en `Id_registros` y otro en `fecha`. **La tabla no tenía ninguno**
  y cada fila implicaba recorrer 14 mil registros.
- Si la API de camiones falla, se avisa en el log y la sincronización de taxis sigue. Los camiones
  no pueden tumbar a los taxis.

Si la API de camiones no responde, el bloque lo registra y sigue de largo; los taxis nunca se
detienen por eso. Así se comportó las horas en que el parche todavía no estaba arriba.

---

### 3.3 El bug de PowerShell que apareció al desplegar (arreglado)

Con la API ya arriba, el primer ciclo real reportó:

```
Camiones recibidos: 1 registros, 1 choferes.
Camiones guardados en SQL Server: 0 registros, 0 choferes.
```

Mismo enredo que en el diagnóstico: **`Invoke-RestMethod` entrega el arreglo JSON como un solo
objeto**, así que `@(...)` lo envuelve en lugar de expandirlo. El bucle recorría una sola vez un
objeto sin `idRegistro` y guardaba cero.

Arreglo: función `Expand-ApiArray` en `sync-sqlserver-hostinger-bidirectional.ps1`, usada en las
dos llamadas de camiones. Probado contra la API real: **44 registros** en vez de 1.

Es un patrón a recordar: cada vez que este proyecto consuma un endpoint que devuelve un arreglo
JSON desde PowerShell, hay que desenvolverlo.

### 3.4 El cuadre ahora lee los camiones de SQL Server, no de la nube

En agosto se puso la API de Hostinger **por delante** de SQL Server en
`LocalPosRepository.GetCamionesResumenAsync`, porque la copia local llegaba tarde y el corte del
día salía con llegadas incompletas. Con el sincronizador bajando camiones cada 5 minutos, esa
razón desapareció, así que el orden se invirtió (04/09):

**SQL Server → API de Hostinger → espejo SQLite.**

Ventaja: más rápido, sin depender de internet, y sigue el mismo camino que todo lo demás — las
máquinas de la tienda leen SQL Server, no la nube.

Cuidado que se tuvo: si el sincronizador llevara horas detenido, SQL Server devolvería el rango
en ceros y el cuadre saldría en $0 sin avisar. Por eso no basta con "¿respondió?": el método
`HasCamionesData` exige que el resumen traiga movimiento real (pax, entraron, salieron, unidades
o dejada distintos de cero). Si viene vacío, cae a la API igual que antes.

## 4. Arreglos al loop del sincronizador

Todos en `SyncTaxi_Plaza28\`:

- **Corte por tiempo**: el ciclo corre en un `Start-Job` y se corta a los `MaxRunSeconds`. Ese
  valor se subió de **180 a 900 s** en `sync.plaza28.config.json`: los ciclos reales tardan
  60-200 s y con 180 se hubiera empezado a cortar trabajo bueno. 900 s sigue recuperando un
  cuelgue en 15 minutos.
- **Estado honesto**: los avisos que el script hijo escupía sin tronar — folio no verificado,
  folio bloqueado, error de API con reintento por curl, "no se marcó nada como sincronizado" —
  ahora llegan a `LastError` y `LastOmittedCount`. Se captura `*>&1` en vez de `2>&1`, así que
  los `Write-Warning` por fin quedan en el log.
- **Disparador de repetición cada 5 minutos** en `instalar-sincronizador-plaza28.ps1`, además del
  `AtStartup`. Si el proceso se muere sin reiniciar la máquina, vuelve solo. No hay riesgo de
  duplicados: el candado del loop revisa el PID vivo y `MultipleInstances IgnoreNew` hace el resto.

### 4.1 Fuga de empaquetado corregida

`ControlTaxiDesktop.csproj` copiaba `SyncTaxi\**\*` sin filtro, así que **`sync.local.config.json`
—la configuración de la máquina de desarrollo: `26.38.252.71\SQLEXPRESS`, usuario `sa`,
`PullOnly`— viajaba dentro de cada paquete de Plaza 28**. Ahora está excluido. Verificado en el
zip nuevo.

---

## 5. Paquete

**`PLAZA28-DESKTOP-20260904-3.zip`** (73.3 MB, 475 archivos), en
`Release-ControlTaxi-Plaza28-Produccion\` y en el escritorio. Sin credenciales, sin `DatosLocal`,
sin `sync.local.config.json`.

**Usar el `-3`.** El `-20260904.zip` a secas se armó antes del arreglo de `Expand-ApiArray`
(sección 3.3) y baja los camiones mal. El `-2` ya está bien; el `-3` sólo agrega la inversión de
fuentes del cuadre de camiones (sección 3.4).

Contenido verificado dentro del zip: `Expand-ApiArray` x3, `Sync-CamionesFromHostinger`,
`Wait-Job` (corte por tiempo), `RepetitionInterval` (disparador cada 5 min),
`MaxRunSeconds: 900`, y el diagnóstico `revisar-sincronizador-plaza28.ps1`.

### Cuidado con la ruta al instalar

En la máquina de Plaza 28 el escritorio **no** está en `DESKTOP TAXIS\` sino en
`DESKTOP TAXIS\SelfContained\Desktop\` — ahí viven `Config\plaza28.credentials.dat` y
`Logs\Plaza28Sync\`, y ahí apunta la tarea programada. Descomprimir en `DESKTOP TAXIS` a secas
crearía una segunda copia sin credencial y dejaría corriendo la vieja.

---

## 6. Pendientes

1. ~~Subir el parche del API de camiones~~ **HECHO el 04/09.** El `index.php` del servidor quedó
   con los dos endpoints; respaldo en `index_bak_2026-09-04.php`. Verificado: `/health` ok,
   registros y choferes respondiendo, y las rutas viejas (`/api/taxis/options`,
   `/api/pos/transportes`, `/api/taxis/registros`) sin romperse. La copia completa de lo que
   quedó arriba está en `Documentacion/index-SERVIDOR-con-camiones-2026-09-04.php`.
2. **Instalar `PLAZA28-DESKTOP-20260904-3.zip`** en las **2 máquinas restantes**. Plaza 28 y la
   de `mkt` quedaron con la `-2`, que funciona; se les pasa la `-3` cuando convenga, para que el
   cuadre de camiones lea de SQL Server. Después, correr
   `INSTALAR-SINCRONIZADOR-PLAZA28.cmd` como administrador para que la tarea quede con el
   disparador de repetición.
3. Sigue pendiente de sesiones anteriores: **instalar los arreglos de comisiones** del 01/09
   (paquete `-20260901-3` nunca instalado; el de hoy ya los trae), revisar los **183 folios de
   TURIBUS SALMORAN y Transporta** pagados con el porcentaje viejo, el sync que no llena
   `mkt2_pos_transports`, y la **divergencia entre la API local y la del servidor**.
4. Hay zips `-20260902` y `-20260903` que nadie documentó; se desconoce qué traen.

---

## 7. Reglas confirmadas en esta sesión

- Los endpoints `/api/pos/*` de Hostinger **no piden token**; los `/sync/*` y `/api/taxis/*` sí
  (header `X-Sync-Token`).
- La tarea programada del sincronizador se llama `ControlTaxi Plaza28 Sync`.
- El respaldo por fecha del sincronizador (`/api/taxis/registros`, últimos 7 días) corre **sólo
  cuando `pull-changes` regresa 0 pendientes**, que es el estado normal. Por eso cada ciclo vuelve
  a procesar la misma semana: es lo que hace que un ciclo tarde más de dos minutos.
- Un registro que nunca se verifica en `dejadas` jamás se marca como sincronizado en Hostinger, y
  se reintenta en cada ciclo. Si se acumularan 500 así, taparían la ventana de `pull-changes` y
  los registros nuevos dejarían de bajar. Hoy hay 0; el diagnóstico avisa si la cola llega al
  límite.
