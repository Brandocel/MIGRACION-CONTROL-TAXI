# Contexto de trabajo — 28 de agosto de 2026

Documento de traspaso para continuar en otra sesión. Resume qué se tocó, por qué, qué quedó
desplegado y qué falta.

---

## 1. Mapa del sistema

| Pieza | Dónde vive | Qué hace |
|---|---|---|
| **Escritorio (WPF, .NET 9)** | `MIGRACION CONTROL TAXI\...\ControlTaxiDesktop` | POS local, Relación Ticket-Taxista, Comisiones, Configuración de comisiones, cuadre/reportes. |
| **App móvil (Flutter)** | `C:\Users\Administrador\taxis_app\taxis_app` | Captura la llegada del taxista (gafete, hotel, pasajeros, **tipo de servicio**). |
| **API PHP** | Hostinger `public_html/index.php` (copia local en `taxis_app\api-php-hostinger`) | Sirve `/api/taxis/*` y `/api/pos/*` a la app móvil, contra MySQL de Hostinger. |
| **SQL Server Plaza 28** | `SERVPLAZA28\SQLEXPRESS`, bases `mkt`, `compuadmo`, `joyeria` | Fuente autoritativa de ventas, registros de app móvil, dejadas, catálogo `dbo.transporte`. |
| **MySQL Hostinger (taxis)** | `u265750591_Taxis` | Espejo para la app móvil: `mkt2_trip_records`, `mkt2_catalog_taxis`, `mkt2_gafetes`, `mkt2_pos_transports`. |
| **MySQL Hostinger (camiones)** | `u679771392_choferes` | Base de la app VB6 de camiones: `choferes` (797), `registros` (14,555), `control`, `tabla_control`, `tabla_control_pax`. |
| **Sync** | `sync-sqlserver-hostinger-bidirectional.ps1` (corre en Windows) | Mueve SQL Server ↔ MySQL. Sólo `mkt2_trip_records`, `mkt2_catalog_taxis`, `mkt2_gafetes`. |

---

## 2. El problema que se investigó

Reporte de operación: la comisión del sistema no cuadra con el Excel de Lupita.

- PANCHO RODRIGUEZ, unidad `GUIA`, 27/08/2026: el sistema pagaba **8%**, el Excel dice **10%**.
- ANTONIO FLORES, folio 4767: un ticket de **$8,584** salía al **10%** cuando el folio es
  TURIBUS SALMORAN (**20%**).

### Causa 1 — hay dos catálogos de transporte y gana el equivocado

El cálculo de Plaza 28 (`LocalPosRepository.ReadTransportCatalogAsync`) arma el catálogo con:

1. `dbo.transporte` del punto de venta, y
2. las reglas de **Configuración de comisiones** (SQLite `CommissionSettingsRules`, **por máquina**).

La regla configurada sólo gana si está **activa y vigente en la fecha del folio** y sólo en la
máquina donde se guardó. Datos reales comparados:

| Unidad | `dbo.transporte` (POS) | Configuración de comisiones |
|---|---|---|
| GUIA / GUIAS | **8 %** | **10 %** |
| VAN TRANSPORTADORAS | 20 % | 10 % |
| TRAVEL EXPERIENCE / MAJESTIC | 20 % | 8 % desde 12/08/2026 |

En la máquina que calculó esos folios la regla configurada no aplicó, así que se usó el 8 % del POS.

### Causa 2 — la unidad llega sucia o vacía desde la app móvil

`AppMovilRegistro.tipo_operacion` traía `GUIA`, `S/N`, `CALLE`, vacío y variantes con errores de
dedo (`TAXI BERDE`, `TEXI VERDE`, `RAXI VERDE`). Origen: en la app el campo **Tipo de servicio**
era texto libre y sus sugerencias salían de `SELECT DISTINCT` de lo ya capturado, no del catálogo.
Sin coincidencia en el catálogo, el escritorio aplicaba el 10 % por omisión.

---

## 3. Cambios hechos

### 3.1 Escritorio — Configuración de comisiones (rediseño)

Archivos: `ControlTaxiDesktop\CommissionSettingsWindow.xaml`, `.xaml.cs`,
`ControlTaxiDesktop\Styles\SharedFilterStyles.xaml`.

- La pantalla usa el mismo diccionario de estilos que Comisiones (encabezado cuadrado, tira de
  totales, tarjeta de filtros, tabla azul, chips). Antes tenía estilos propios redondeados.
- Se quitaron los botones falsos de ventana (`_ □ X`) que duplicaban los de Windows.
- Pestañas planas con subrayado. Se cambió `TabPanel` por `StackPanel` en la plantilla porque
  `TabPanel` recortaba el texto ("CATÁL...", "HISTO...").
- Estilos nuevos en el diccionario compartido: `DashboardGridHeader/Cell/Row`, `SquareInputBox`,
  `SquareInputLabel`, `SquareSectionHeading`, `SquareComboInput`, `SquareTabItem`,
  `SquareTabControl`. Los botones fijan el color del texto dentro del `ContentPresenter` porque
  el estilo global de `TextBlock` (App.xaml) le ganaba a la herencia.
- **El editor salió del costado**: ahora es un panel emergente (`EditorOverlay`) que abre EDITAR o
  el doble clic. Cierra con CERRAR, CANCELAR o Esc. La tabla ocupa todo el ancho.
- Se corrigió que la etiqueta "Tipo de pax" quedaba sobre el campo de N° de moneda.
- La tarjeta USANDO FALLBACK abría "Reglas globales" (índice 3); ahora abre "Opciones avanzadas"
  (índice 4), que es donde están los diagnósticos.

### 3.2 Escritorio — editar corrige la misma regla

Archivo: `Services\CommissionSettingsRepository.cs` (nuevo `UpdateRuleAsync`).

`SaveRuleAsync` versiona: cierra la regla anterior e inserta otra, y exige que la nueva vigencia
empiece después. Eso servía para "de hoy en adelante cobramos otro porcentaje", pero no para
corregir un dato mal capturado. Ahora:

- **EDITAR** → `UpdateRuleAsync`: actualiza la misma fila (mismo `Id`), sin exigir vigencia nueva.
  El historial se conserva: el cambio se sigue escribiendo campo por campo en
  `CommissionSettingsAudit`.
- **CAMBIAR COMISIÓN** → la fecha viene precargada con la vigencia actual, así que por omisión
  corrige. Si el operador pone una fecha posterior, entonces sí versiona (cierra la vieja, nace
  la nueva) y el mensaje de confirmación lo dice.

Verificado contra base de prueba: 6 reglas antes y después, `Id=10` pasó de 8 % a 11 % con la
misma vigencia, 1 fila de auditoría.

### 3.3 Escritorio — cómo se busca la unidad en el catálogo

Archivo: `Services\LocalPosRepository.cs`.

- `SelectTransportRuleByScore` + `ScoreTransportCandidate`: se elige por puntaje (exacto 100,
  prefijo 70-95, contenido 58-88, errores de dedo por distancia de Levenshtein). Umbral 62; por
  debajo **no empareja**, porque cobrar la comisión de otra unidad parecida es peor que no
  encontrarla. Mata además un bug: una regla con clave o nombre vacío emparejaba con todo, porque
  `"GUIA".Contains("")` siempre es verdadero.
- `InheritTransportAcrossTickets`: los renglones del mismo folio que llegan sin unidad copian la
  del renglón que sí la trae (un folio es una llegada y trae un solo transporte). Sólo completa
  hacia los que no resolvieron regla, nunca pisa una unidad reconocida.

Prueba con el catálogo real (23 unidades del POS + 17 configuradas), fecha 27/08/2026:

```
"GUIA"              -> 10 %   (antes 8 %)
"TURIBUS SALMORAN"  -> 20 %   "SALMORAN" -> 20 %
"TAXI  VERDE"       -> 10 %   "TAXOVERDE" -> 10 %   "VAN TRANSPOTADORA" -> 10 %
"S/N" / "" / "CALLE"-> sin regla (ya no inventa unidad)
```

**Los folios pasados se recorrigen solos**: el escritorio no guarda el porcentaje, lo recalcula en
cada consulta. Y como el pago guarda el importe real en `pago_comision`, un folio pagado al 8 %
cuya comisión correcta es 10 % aparece como **PARCIAL** con su saldo, listo para pagar la
diferencia desde la misma pantalla.

Caso que sí puede esconder faltantes: folios con fecha de pago pero importe 0 — el sistema los da
por pagados completos. Consulta para detectarlos en SQL Server (`mkt`):

```sql
SELECT folio_app, folio_app_original, fecha_operacion, vendedor_nombre,
       tipo_operacion, pago_comision, fecha_pago_comision
FROM dbo.AppMovilRegistro
WHERE fecha_pago_comision IS NOT NULL
  AND COALESCE(pago_comision, 0) = 0
ORDER BY fecha_operacion DESC;
```

### 3.4 App móvil — el tipo de servicio sale del catálogo

- `api-php-hostinger\index.php`, endpoint `/api/taxis/options`: `serviceTypes` ahora sale de
  `mkt2_pos_transports` (espejo de `dbo.transporte`). Si esa tabla está vacía, cae al
  comportamiento anterior para no dejar la app sin lista.
- `lib\screens\registration\new_registration_screen.dart`: el campo **Tipo de servicio** dejó de
  ser texto libre. Abre un selector con buscador y sólo acepta unidades del catálogo. Además:
  ya no preselecciona la primera unidad de la lista; al editar un registro con unidad fuera de
  catálogo la marca en rojo; y el guardado se bloquea si la unidad no es del catálogo.

### 3.5 Despliegues hechos hoy

- **API**: subida a `public_html/index.php`. Se parcheó **sobre el archivo del servidor**, no se
  reemplazó con la copia local (habían divergido). Respaldo en `index_bak_2026-08-28.php`.
- **MySQL**: `mkt2_pos_transports` estaba **vacía** — el sync que corre en producción nunca la
  llenó. Se cargaron 31 filas (29 nombres) uniendo `dbo.transporte` con las reglas vigentes de
  Configuración de comisiones. `/api/taxis/options` ya responde el catálogo limpio.
- **APK**: `v1.0.12+13`, compilado y probado en la tableta Lenovo TB 8505F.
  Copia para repartir: `C:\Users\Administrador\Downloads\HOKA_TAXIS_v1.0.12_2026-08-28.apk`.
  Para compilar hubo que subir: Flutter a 3.47.2, Gradle 8.12 → **8.14**, AGP 8.9.1 → **8.11.1**,
  Kotlin 2.1.0 → **2.2.20** (archivos `android/gradle/wrapper/gradle-wrapper.properties` y
  `android/settings.gradle.kts`).
- **Escritorio**: paquete `PLAZA28-DESKTOP-20260828.zip` (73 MB) en
  `Release-ControlTaxi-Plaza28-Produccion\` y copiado al escritorio. Trae los
  `appsettings.production.json` / `branches.production.json` de producción; **no** trae
  `DatosLocal` ni la credencial cifrada (es por máquina, DPAPI).

  Instalación en cada equipo:

  ```powershell
  $d=[Environment]::GetFolderPath('Desktop'); Get-Process ControlTaxiDesktop -EA SilentlyContinue | Stop-Process -Force; Expand-Archive -Path "$d\PLAZA28-DESKTOP-20260828.zip" -DestinationPath "$d\DESKTOP TAXIS" -Force
  ```

---

## 4. Pendientes

1. **Probar el escritorio nuevo en la máquina de prueba** y luego pasar a las máquinas de las
   chicas. Revisar ahí: (a) que la regla `GUIA` esté activa, 10 %, vigencia 01/01/2026 sin fecha
   fin; (b) folio de PANCHO RODRIGUEZ 27/08 → 10 % y PARCIAL con saldo si ya se pagó al 8 %;
   (c) folio 4767 de ANTONIO FLORES → el ticket de $8,584 al 20 %.
2. **Decisión pendiente**: cuando la unidad tiene regla configurada pero ninguna vigencia cubre la
   fecha del folio, ¿se usa la vigencia más cercana (el catálogo configurado manda siempre) o se
   sigue cayendo a `dbo.transporte`? Alternativa menor: sólo mostrar el conflicto como diagnóstico
   en Opciones avanzadas, sin cambiar cálculos.
3. **El sync no llena `mkt2_pos_transports`**. Hoy se cargó a mano; hay que agregar ese bloque a
   `sync-sqlserver-hostinger-bidirectional.ps1` (el de `api-php-hostinger-mysql\windows-sync\`
   ya tiene la consulta, sirve de referencia) o cada unidad nueva del POS habrá que cargarla a mano.
4. **Divergencia API local vs servidor**: la copia local tiene cambios que nunca subieron
   (monto sugerido en catálogo de taxis, arreglo de `driverKey` para Plaza 28, renombrar hoteles
   en `mkt2_hotels`) y el servidor tiene cosas que la copia local no (una función `body()` más
   tolerante). Hay que alinearlas con calma.
5. **Camiones / AUTOCAR desde Hostinger** — el código ya está escrito y compila; faltan los
   pasos de servidor y el redespliegue. Ver sección 5.

---

## 5. Camiones (AUTOCAR, MAYA CARIBE, TURICUN) desde Hostinger — EN CURSO

**Estado: el código ya está escrito y compila. Falta configurar el servidor y desplegar.**

**Lo que se quiere:** que el reporte/cuadre tome los buses directamente de la base MySQL de
Hostinger, en vez de depender de SQL Server local, porque la copia en SQL Server **llega tarde**
y el corte del día salía con llegadas incompletas.

> **Ojo — hay dos cuentas de Hostinger.** La API y la base de taxis (`u265750591_Taxis`) están en
> una cuenta; la base de camiones (`u679771392_choferes`) está en **otra**. Por eso la conexión es
> por host remoto (`193.203.166.19`) y necesita permiso explícito. (El usuario también maneja dos
> cuentas de Claude, por eso este documento vive en el proyecto.)

### 5.1 Estructura confirmada (consultada el 28/08/2026)

`registros` (MySQL) — mismos campos que `registroscamiones` de SQL Server, cambia la capitalización:

| MySQL `registros` | Tipo | SQL Server |
|---|---|---|
| `Id_chofer` | varchar(10) | id_chofer |
| `Pax`, `Pax_valido` | int(11) | pax, pax_valido |
| `Camion` | varchar(10) | camion |
| `Comision` | int(11) | comision |
| `Fecha` | **date** | fecha |
| `Hora` | time | hora |
| `Id_registros` | int PK auto_increment | ld_registros |

`choferes` (MySQL): `Id_chofer` varchar(10) NOT NULL, `Clave`, `Nombre`, **`Empresa`** varchar(100),
`Grupo`, `NumeroTarjeta`, `Banco`, `Telefono`, `Comentario`, `activo` varchar(1).

Llegadas del **28/08/2026** en Hostinger: AUTOCAR — 223 pax, 178 entraron, 45 salieron,
41 unidades, $2,720 de dejada, 45 registros. MAYA CARIBE y TURICUN sin movimiento ese día.

### 5.2 Lo que ya se programó

- `ControlTaxiDesktop\Services\PlazaCamionesApiService.cs` (**nuevo**): pide el resumen a
  `api/pos/camiones?desde=&hasta=` y lo mapea a `LocalCuadreResumenRow` con el mismo orden de
  columnas que la consulta de SQL Server (Concepto, Pax, Entraron, Salieron, Unidades, Dejada,
  0, 0, 0, Neto=Dejada). La URL sale de `BranchConfigurationService.GetBranch("P28").ApiBaseUrl`.
- `LocalPosRepository.GetCamionesResumenAsync`: ahora el orden de fuentes es
  **API Hostinger → SQL Server → espejo SQLite**. Si la API falla (sin internet, sin permiso
  remoto) se sigue con las de siempre para no dejar el cuadre vacío.
- `api-php-hostinger\index.php`: endpoint `/api/pos/camiones` (dentro de `route_pos_get`) con las
  fórmulas del cuadre, más la función `camiones_db()` que abre la conexión a la otra cuenta
  leyendo `config.php`. Sin `desde`/`hasta` responde el día de hoy.
- `api-php-hostinger\config.php`: bloque nuevo `mysql_camiones` con host/base/puerto puestos y
  **usuario y contraseña como marcadores** — hay que llenarlos con los de la app VB6.

### 5.3 Pasos que faltan para dejarlo funcionando

1. En el hPanel de la cuenta **u679771392** (choferes): Bases de datos → **MySQL remoto** →
   permitir el host de la otra cuenta (o `%` de forma temporal).
2. En `public_html/config.php` de la cuenta de la API: pegar el bloque `mysql_camiones` con el
   usuario y la contraseña reales.
3. Bajar el `index.php` del servidor, aplicarle el parche del endpoint (**no** subir la copia
   local completa, siguen divergidas) y subirlo.
4. Verificar: `https://lightyellow-porpoise-679527.hostingersite.com/api/pos/camiones` debe
   responder lo mismo que phpMyAdmin.
5. Contrastar contra el Excel del receptivo pidiendo `?desde=2026-08-27&hasta=2026-08-27`:
   el Excel de ese día trae AUTOCAR 292 pax, 237 entraron, 55 salieron, 37 unidades, $3,500.
   Si no cuadra, revisar las fórmulas antes de dar por buena la fuente.
6. Reempaquetar el escritorio (el `PLAZA28-DESKTOP-20260828.zip` actual **no** trae este cambio,
   se armó antes) y reinstalar donde toque.

### 5.4 Cómo funcionaba antes (fuente que se está reemplazando)

**SQL Server** (`LocalPosRepository.GetCamionesResumenAsync`, línea ~1354):

- Fuente oficial: `dbo.registroscamiones` unida a `dbo.choferes` por `id_chofer`, en **SQL Server**.
- La categoría sale de `choferes.empresa`: `AUTOCAR` → ACAR, `MAYA CARIBE` → MC, `TURICUN` → TUR.
- Fórmulas del cuadre: `PAX = SUM(pax_valido)`, `SALIERON = COUNT(registros con pax_valido > 0)`,
  `ENTRARON = PAX - SALIERON`, `UNIDADES = COUNT(DISTINCT camion)`, `DEJADA = SUM(comision)`.
- Si SQL Server no responde, cae al espejo SQLite (`mkt__dbo__registroscamiones`, `mkt__dbo__choferes`).
- **No** usar `dejadas.tipotransporte` para estos: desde la migración de junio 2026 ya no guarda
  AUTOCAR / MAYA CARIBE / TURICUN.

**La base de Hostinger** (`u679771392_choferes`, la que usa la app VB6 de camiones vía MariaDB ODBC):

| Tabla | Filas | Notas |
|---|---|---|
| `choferes` | 797 | Aquí vive `empresa`, la que clasifica ACAR / MC / TUR. |
| `registros` | 14,555 | Equivale a `registroscamiones` de SQL Server. |
| `control` | 1 | |
| `tabla_control` | 2 | |
| `tabla_control_pax` | 3 | |

**Qué falta definir antes de programar:**

1. Mapeo de columnas `registros` (MySQL) ↔ `registroscamiones` (SQL Server): `id_chofer`, `pax`,
   `pax_valido`, `camion`, `comision`, `fecha`, `hora`.
2. Cómo llega el escritorio a esa base: ¿un endpoint nuevo en la API PHP (recomendado, ya hay
   infraestructura y no expone MySQL), o conexión directa desde el escritorio?
3. Qué manda cuando ambas fuentes tienen datos del mismo día: ¿Hostinger o SQL Server?

**Referencia del corte del 27/08/2026** (Excel del receptivo) para validar lo que se programe:
AUTOCAR 292 pax, 237 entraron, 55 salieron, 37 unidades, dejada $3,500. MAYA CARIBE y TURICUN en
cero ese día. Totales del día: 498 pax, 88 unidades, dejada $17,100, comisión $9,445,
venta $93,089.

---

## 6. Comandos útiles

```bash
# Compilar el escritorio (cerrar la app antes; Release no choca con la instancia abierta)
dotnet build "…\ControlTaxiDesktop\ControlTaxiDesktop.csproj" -c Release

# Publicar self-contained para armar el paquete
dotnet publish "…\ControlTaxiDesktop\ControlTaxiDesktop.csproj" -c Release -r win-x64 --self-contained true -o <carpeta>

# App móvil
flutter analyze lib/screens/registration/new_registration_screen.dart
flutter build apk --release
flutter install --release -d <idDispositivo>

# Verificar el catálogo que ve la app
curl "https://lightyellow-porpoise-679527.hostingersite.com/api/taxis/options"
curl "https://lightyellow-porpoise-679527.hostingersite.com/api/pos/transportes?database=Mkt2"
```

---

## 7. Reglas de negocio confirmadas (no romper)

- **Relación Taxista paga sólo la DEJADA; Comisiones paga sólo la COMISIÓN.** Nunca mezclar los
  dos flujos.
- La **dejada del folio se carga completa al ticket de mayor venta**, no se reparte proporcional.
- Manda **lo capturado en Relación Taxi**, no el tabulador del catálogo: el tabulador es la
  referencia de cuánto debería ser.
- Por debajo del umbral de venta configurado en "Reglas globales", la dejada **no** se descuenta
  de la base de comisión.
- En el punto de venta, la forma de pago real es el **número de moneda**, no el texto.
- Cada equipo guarda su propia copia de `CommissionSettingsRules` en su SQLite local: los cambios
  de tasas hay que repetirlos máquina por máquina.
- El zip de despliegue **no** debe incluir `DatosLocal\ControlTaxi.db`: sobrescribe la base local.
