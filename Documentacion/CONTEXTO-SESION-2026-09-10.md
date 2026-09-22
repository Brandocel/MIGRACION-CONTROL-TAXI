# Contexto de sesión — 2026-09-10

## El problema

La tablet captura los vendedores que atendieron una llegada, uno por gafete (folio 5513:
`LUIS CRUZ - Gafete 205 | ABRAHAM JUAREZ BOTELLO - Gafete 217`), y en el historial de la app
sí se ven. En el escritorio no: la columna **Vendedor** de LIBERACION DE GAFETES sale vacía y
la columna **Vendedores / Gafetes** de RELACION DE REGISTROS también.

## Por qué no llegaba

Se siguió el dato de punta a punta y se rompía en dos lugares distintos:

1. **El API de Hostinger no manda los pares por la ruta que usa el sincronizador.**
   La versión con vendedores del `index.php` (`PENDIENTE-VENDEDORES-GAFETES-20260905/api/
   index_vendedores_gafetes_2026-09-09.php`, que es la que hoy corre en el servidor: la ruta
   `/api/taxis/registros` ya responde `sellerBadges`) guarda los pares en la tabla
   `mkt2_trip_record_sellers` y los pega con `attach_trip_record_sellers()` en
   `/api/taxis/registros` y en el detalle por id. Pero **`/sync/pull-changes` no los pega**:
   devuelve el `SELECT` de `mkt2_trip_records` a secas. El sincronizador de Plaza 28 baja los
   registros nuevos por `pull-changes`, así que los recibía sin `sellerBadges`, y ese es el
   objeto que se guarda como `detalle_json` en `dbo.AppMovilRegistro`. El escritorio arma la
   columna "Vendedores / Gafetes" leyendo justo `detalle_json.sellerBadges`
   (`FormatSellerBadges` en `LocalOperationsRepository.cs`), así que quedaba vacía.

2. **El sincronizador nunca escribía `dbo.gafete.vendedor`.** La pantalla de Gafetes lee la
   columna `vendedor` de `dbo.gafete` (`GetBadgesAsync` / `FindBadgeFromSqlServerAsync`). El
   botón GUARDAR del escritorio sí la llena, pero `Save-GafeteInsertOnly` del
   `sync-sqlserver-hostinger-bidirectional.ps1` insertaba
   `(matricula,gafete,fecha,venta,hora,folioperacion)` y nada más. Aunque el punto 1 se
   arreglara, la columna Vendedor de esa pantalla seguiría en blanco.

Detalle que ayuda: cuando `pull-changes` no trae pendientes, el sincronizador cae a
`/api/taxis/registros?dateFrom=&dateTo=` (últimos 7 días) y vuelve a pasar esos registros por
`Save-AppMobileRegistro` / `Save-GafeteInsertOnly` en cada ciclo. Esa ruta **sí** trae
`sellerBadges`. Por eso el arreglo del sincronizador solo ya rellena lo de hoy sin tocar nada
a mano.

## Qué se cambió

### `SyncTaxi_Plaza28/sync-sqlserver-hostinger-bidirectional.ps1` (+53 líneas, nada borrado)

Respaldo previo en `SyncTaxi_Plaza28/respaldos/sync-sqlserver-hostinger-bidirectional-20260910-antes-vendedor.ps1`.

- `Ensure-AppTables`: asegura `dbo.AppMovilRegistro.detalle_json` y **`dbo.gafete.vendedor
  NVARCHAR(150) NULL`** si no existen (el bloque de `Save-GafeteInsertOnly` también lo asegura,
  por si corre antes).
- Funciones nuevas `Test-RecordHasSellerBadges` y `Get-SellerForBadge`: dado el registro y un
  gafete, regresa el vendedor del par cuyo `badgeId` coincide (comparando sin ceros a la
  izquierda). Si el registro no trae pares (registro viejo o espejo local), usa `sellerName`
  únicamente cuando la llegada tuvo **un solo** gafete; con varios gafetes y sin pares no
  adivina.
- `Save-GafeteInsertOnly`: el `INSERT` a `dbo.gafete` ahora lleva `vendedor`, y después hace un
  `UPDATE ... SET vendedor = @vendedor WHERE folioperacion = @folioControl AND gafete =
  @badgeNumber AND vendedor vacío`. Ese `UPDATE` es el que rellena las filas que ya existían
  (las de hoy, y cualquier registro cuyo primer pull llegó sin pares). No pisa un vendedor que
  ya esté puesto.
- `Save-AppMobileRegistro`, rama `UPDATE`: agrega
  `detalle_json = CASE WHEN @hasSellerBadges = 1 THEN @detailJson ELSE detalle_json END`.
  Solo se refresca el detalle cuando el registro entrante trae `sellerBadges`; los espejos
  locales de `Save-RecentLocalLegacyMirrors` (que arman un registro reducido desde SQL Server)
  no lo tocan. Antes la rama `UPDATE` nunca actualizaba `detalle_json`, así que un registro
  guardado sin pares se quedaba sin pares para siempre.

Probado en local: el script parsea sin errores y `Get-SellerForBadge` con el JSON real del
folio 5513 devuelve `217 → ABRAHAM JUAREZ BOTELLO`, `205 → LUIS CRUZ`, `0205 → LUIS CRUZ`,
gafete inexistente → vacío, registro viejo de un gafete → su `sellerName`. **No se pudo probar
contra SQL Server**: `SERVPLAZA28` no responde desde esta máquina y la credencial DPAPI de
`DESKTOP TAXIS\Config` está cifrada para otra máquina.

Copias del script parchado: en el repo, en `Desktop\DESKTOP TAXIS\SyncTaxi_Plaza28` de esta
máquina y en `Desktop\sync-sqlserver-hostinger-bidirectional-20260910-vendedores.ps1` para
llevarlo a Plaza 28.

### API de Hostinger — parche pendiente de subir

`Documentacion/aplicar-parche-pull-sellerbadges.ps1`: envuelve el `SELECT` de
`mkt2_trip_records` de `/sync/pull-changes` con `attach_trip_record_sellers(...)`. Una línea.
Ancla por la cadena exacta del `SELECT` (aparece una sola vez), verifica que el archivo tenga
`attach_trip_record_sellers`, respalda como `index.php.backup-<fecha>` y aborta si ya está
aplicado. `Documentacion/index-servidor-20260910-pull-sellerbadges.php` es la copia local ya
parchada, por si se prefiere subir el archivo completo — **ojo: es la versión con vendedores
del 09/09, incluye el parche del cuadre; comparar con el del servidor antes de reemplazar**.

Sin este parche el sincronizador igual termina bien por la ruta de "recientes", pero el primer
ciclo de cada registro nuevo llega sin vendedores y se corrige en el siguiente (15 s).

### Escritorio — cómo se ve el vendedor (segunda parte de la sesión)

Con el dato ya llegando, la columna Vendedor de LIBERACION DE GAFETES lo cortaba
("ABRAHAM JUA") y no se notaba cuándo dos gafetes eran de la misma llegada. Cambios, sin
tocar consultas ni flujo:

- `Models/LocalOperationsModels.cs`: `LocalBadge` gana `VendorTeam` (se llena al cargar),
  `VendorDisplay` y `VendorTooltip`.
- `OperationsWindow.xaml.cs`: `FillBadgeVendorTeams` agrupa las filas ocupadas por folio de
  operación y a cada una le pone "Con LUIS CRUZ (205)" con los demás gafetes de la llegada.
  Se llama en las dos cargas de `BadgesGrid` (SQL Server y Casco).
- `OperationsWindow.xaml`: la columna Vendedor pasa a plantilla —nombre completo con salto de
  línea, segunda línea chica con el equipo (oculta si no hay), tooltip— y el `DataGrid` usa
  `MinRowHeight="52"` en vez de `RowHeight="52"` para que quepan dos líneas. En RELACION DE
  REGISTROS la columna "Vendedores / Gafetes" muestra un vendedor por renglón
  (`LocalRegistroDiarioRow.VendedoresLineas`, en `LocalReportModels.cs`).

Compila sin errores. Paquete: `PLAZA28-DESKTOP-20260910-2.zip` (73 MB), copia en el escritorio
de esta máquina. Instalar con la guía de siempre; trae el `.ps1` del sincronizador ya parchado,
así que en máquinas donde no se copió a mano también queda.

## Cómo instalarlo en Plaza 28

Solo cambia un archivo. El ciclo del sincronizador arranca el script en un job nuevo cada vez,
así que basta reemplazarlo; no hay que reinstalar la tarea ni reiniciar.

```powershell
$d=[Environment]::GetFolderPath('Desktop')
Copy-Item "$d\sync-sqlserver-hostinger-bidirectional-20260910-vendedores.ps1" "$d\DESKTOP TAXIS\SyncTaxi_Plaza28\sync-sqlserver-hostinger-bidirectional.ps1" -Force
```

Verificar en el siguiente ciclo (`REVISAR-SINCRONIZADOR-PLAZA28.cmd` o el log en
`DESKTOP TAXIS\Logs\Plaza28Sync\plaza28-auto-sync.log`) que no salga `No se pudo insertar gafete nuevo`. Luego abrir
LIBERACION DE GAFETES: los gafetes 217 y 205 del folio 5513 deben traer vendedor.

Consulta directa si se quiere confirmar en SQL:

```sql
SELECT gafete, folioperacion, venta, vendedor FROM dbo.gafete WHERE folioperacion = '5513';
SELECT folio_app, LEFT(detalle_json, 400) FROM dbo.AppMovilRegistro WHERE folio_app = '5513';
```

## Cosa vista de paso, no tocada

`Save-RecentLocalLegacyMirrors` (solo con `-RunLocalMirrorReview`) arma un registro reducido
sin `sellerName` y lo pasa por la rama `UPDATE`, que hace `seller_name = @sellerName`: dejaría
`seller_name` en blanco en esos registros. No corre en el ciclo normal. Queda anotado.

## Pendientes

- Subir el parche de `pull-changes` al API (`aplicar-parche-pull-sellerbadges.ps1`).
- `.ps1` ya copiado y verificado en la máquina principal de Plaza 28 (SQL devolvió
  `205 → LUIS CRUZ`, `217 → ABRAHAM JUAREZ BOTELLO` y la pantalla lo mostró). Falta la
  segunda máquina, o instalar ahí el paquete `-2` que ya lo trae.
- Instalar `PLAZA28-DESKTOP-20260910-2.zip` en las 2 máquinas (mejora visual del vendedor).
- Casco Viejo sigue sin el parche del cuadre (`/casco-api/`), pendiente del 09/09.
