# Contexto de trabajo — 24 de septiembre de 2026

Gafetes impresos con código de barras para **Casco Viejo**, y limpieza de disco en el servidor de
desarrollo.

Continuación de `CONTEXTO-SESION-2026-09-19.md`.

---

## 1. Por qué

En Plaza 28 el vendedor trae un gafete físico con código de barras, y ese número es lo que amarra
la venta con el conductor. En Casco Viejo no hay presupuesto para mandar hacer gafetes, así que el
mismo número se imprime en un talón de 80 mm y se lee con el mismo escáner.

**El sistema no inventa números.** Imprime el gafete que el viaje ya trae capturado, así que
imprimir dos veces no cambia nada en la base.

## 2. Qué se hizo

- `Services/Code128Barcode.cs` — Code 128 juego B, propio (el escritorio se publica
  self-contained y sin internet en sucursal, por eso no se agregó un paquete). Se dibuja con
  rectángulos de WPF, no como imagen: al imprimir sale con la resolución de la impresora.
- `Services/BadgeTicketBuilder.cs` — arma el talón (80 mm): sucursal, número grande, código de
  barras, vendedor, taxista, unidad, hotel, folio, fecha y pax. Un talón por gafete, con línea de
  corte entre uno y otro. Si el viaje no trae vendedores capturados pero sí el gafete del taxista,
  imprime ese.
- `BadgeTicketPreviewWindow` — vista previa antes de mandar a la impresora. El documento se arma
  dos veces a propósito: un `FlowDocument` no puede estar en el visor y en la impresión a la vez.
- `OperationsWindow` — botón **GAFETES** en cada renglón de RELACIÓN TICKET - TAXISTA. Sólo
  aparece en Casco Viejo: `Services/BranchUiFlags.cs` guarda la bandera y se prende en el
  constructor **antes** de `InitializeComponent`, porque los botones viven en una plantilla de
  renglón y no ven a la ventana. Si la sesión abre con sucursal `ALL`, la bandera queda apagada.

## 3. Verificación del código de barras

No se dio por bueno "porque se ve bien": los PNG generados por el propio motor se leyeron con
**zbar** (`pyzbar`), que es un lector independiente.

- Tabla de 107 símbolos: todos de 11 módulos (el de paro, 13), sin repetidos.
- Decodificados correctamente como CODE128: `207`, `258`, `9`, `1234`, `CV-000123`, `A1B2C3`,
  tanto en grande como al tamaño real del ticket (módulo 1.6, 203 ppp de impresora térmica).
- El talón completo renderizado (dos gafetes en una hoja) también se lee: devuelve `207` y `258`.

Ancho: un gafete de 3 dígitos mide 141 puntos y uno de 9 caracteres 246, dentro de los 272 útiles
de un ticket de 80 mm.

## 4. Diseño de la tabla de viajes

- La barra de desplazamiento vertical se montaba sobre la última columna y cortaba los botones de
  acción: se le dejó margen derecho a la tabla.
- La celda de acciones pasó a tres renglones (EDITAR/VENTA, PAGAR/IMPRIMIR, GAFETES) y la altura
  de renglón subió de 112 a 124.
- Los anchos mínimos de las columnas suman 920 puntos: la tabla entra completa desde 1024 de
  ancho; abajo de eso se recorre de lado.

## 5. Disco lleno en el servidor de desarrollo

C: estaba en **0 GB** y por eso fallaban compilaciones y comandos. Con autorización del usuario se
borraron: 46 paquetes viejos (3.5 GB, se dejaron `PLAZA28-DESKTOP-20260919-5.zip` y el del 17/09),
la carpeta Temp (1.3 GB), `compuadmo4_migracion.bak` del escritorio (11.3 GB) y las compilaciones
Debug. Quedaron **39 GB libres**. Conviene no acumular un zip por prueba: cada uno pesa 73 MB.

## 6. Varios gafetes en un solo campo

`AppMovilRegistro.folio_gafete` guarda TODOS los gafetes del viaje en un mismo campo separados por
coma ("207, 258, 251"). Al revisarlo en la base local se vio que habría salido **un solo talón con
un código inventado** ("207,258,251"). Ahora se parten con `BadgeSelectionWorkflow.SplitScanValues`
(el mismo partidor que usa el escáner) y sale un talón por número. Lo mismo cuando un vendedor trae
varios gafetes en `sellerBadges`.

## 7. Prueba local en el equipo de desarrollo

Carpeta `C:\CASCO PRUEBA\` (fuera del repositorio), contra el SQL Server de esta máquina:

- `PREPARAR-PRUEBA-CASCO-LOCAL.cmd` — crea un login de SQL Server sólo de lectura
  (`controltaxi_prueba`) sobre mkt/compuadmo/joyeria y el usuario del programa en
  `mkt.dbo.ControlTaxiUsuarios` con `BranchCode = CV`. Las contraseñas se teclean ocultas.
- `PROBAR-CASCO-LOCAL.cmd` — exporta `CASCO_SQL_*` y abre el escritorio con
  `CONTROL_TAXI_START_CASCO_SERVICES = false`, para que la prueba **no toque Hostinger de
  producción**. El `branches.production.json` de esa copia apunta a `.\SQLEXPRESS` con
  `ApiBaseUrl` vacío y se llama "Casco Viejo (PRUEBA LOCAL)".

- `crear-viajes-de-prueba.sql` — tres viajes inventados con `sitio = 'Casco Viejo'`
  (CV-PRUEBA-1 con tres gafetes, CV-PRUEBA-2 con uno, CV-PRUEBA-3 con vendedor por gafete en
  `detalle_json`). La pantalla de Casco filtra por `a.sitio = @sitio`, y la base local sólo tenía
  viajes de Plaza 28: por eso BUSCAR salía vacío. Se borran con
  `DELETE FROM dbo.AppMovilRegistro WHERE folio_app LIKE 'CV-PRUEBA-%'`.
- La contraseña del login de prueba se guarda cifrada con DPAPI en `clave-sql-prueba.dat` (atada a
  ese usuario de Windows y a esa máquina), así no hay que teclearla cada vez ni queda en texto.

En el repositorio quedó `ControlTaxiDesktop/SyncTaxi/crear-usuario-controltaxi.ps1`, que crea o
actualiza un usuario en cualquiera de las dos sucursales (mismo hash PBKDF2 que el programa).
Ojo con PowerShell 5.1: no tiene `RandomNumberGenerator::Fill` ni `Rfc2898DeriveBytes::Pbkdf2`
(son de .NET moderno); el hash se arma con `RNGCryptoServiceProvider` y el constructor de
`Rfc2898DeriveBytes`. Se verificó que el hash generado así lo acepta `VerifyPassword` del programa.

## 8. El usuario de SQL de Casco estaba escrito a mano

`CascoReadOnlyDataProvider`, `CascoBadgeProvider`, `CascoPosSaleLinkService` y
`CascoSalesDataProvider` abrían la conexión con `UserID = "sa"` fijo. Con eso se podía entrar al
programa (el login sí respeta `SqlUser` de la sucursal) pero cualquier consulta fallaba con
"Login failed for user". Ahora todos usan `Services/CascoSqlIdentity.ResolveUser(branch)`:
variable `CASCO_SQL_USER`, luego `SqlUser` de `branches.production.json`, y al final `sa`.
**En producción no cambia nada**, porque la credencial cifrada publica `CASCO_SQL_USER = sa`.

## 9. Paquete

`PLAZA28-DESKTOP-20260924-4.zip` (73.4 MB, sin credenciales). Mismo paquete para Plaza 28 y Casco;
trae también todo lo del 19/09 (Majestic con $500 por llegada y el pago de folios con ticket en
negativo).

## 10. Pendientes

1. Probar la impresión real en la impresora de Casco y escanear un talón con el lector de la
   tienda.
2. Definir si el talón debe llevar algo más (logo, "no válido sin sello", etc.).
3. Lo que ya venía: Farmacias en Casco, hoja MATILDE, y el parche de cuadre para `/casco-api/`.
