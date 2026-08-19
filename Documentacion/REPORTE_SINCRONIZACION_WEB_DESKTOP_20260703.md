# Reporte de sincronizacion Web -> Desktop

Fecha: 2026-07-03

Fuente mas reciente tomada como base:

- `C:\Users\reyna\Downloads\PosSqlMirrorService.AppMovilSync.cs-main (2).zip`
- `Backup/WebActualizado_20260630/PosSqlMirrorService.AppMovilSync.cs-main`

## Alcance revisado

Se revisaron y contrastaron:

- `Controllers`
- `Services`
- `Data`
- `Interfaces`
- `Models`
- `Views`
- `wwwroot`
- `Program.cs`
- `appsettings*.json`
- importador `ControlTaxiDesktop.Tools`
- ventanas y repositorios WPF locales
- documentacion previa de diferencias y actualizaciones

## Funcionalidades nuevas/localizadas en la version Web mas reciente

1. Ajustes nuevos en `Registro App movil`
   - folio control para folios Web/App
   - persistencia espejo de `AppMovilRegistro`
   - persistencia por multiples gafetes en `AppMovilRegistroGafetes`
   - busqueda/catalogos enriquecidos para taxistas, tarifas y hoteles
   - validacion relajada para operaciones tipo Majestic/Maestic

2. Relaciones / dejadas / pagos
   - priorizacion de folios y ticket de pago
   - mejor cruce entre `AppMovilRegistro`, `RelacionTicketTaxista` y `dejadas`
   - estatus de pago dejada y datos de usuario/fecha/ticket
   - flujo donde el pago/captura en `0` ya no debe bloquear el guardado cuando aplica pago total pendiente

3. Comisiones
   - reglas especiales Majestic 8%
   - reglas autoritativas Salmoran 20%, Majestic/Travel Experience 8%, VANTR 10% y resto 10%
   - descuento tarjeta 19%
   - descuento AMEX 21%
   - deducciones operativas por ticket desde `egresos` / `gastos`
   - aplicacion de dejada sobre la venta mayor del grupo
   - uso de tickets reales de `remisioM`
   - exclusion de tickets `BF*` para Salmoran/Turibus
   - abono con `0` interpretado como liquidacion del saldo restante cuando aplica

4. Reportes/exportaciones
   - cambios de texto `Salieron`
   - ajuste de columnas en exportaciones de relaciones/dejadas

5. Relacion Ticket-Taxista
   - comision tomada desde tickets reales ligados al folio
   - columna de comision separada del pago/dejada
   - estado `PAGADA` basado en pago real de comision
   - filtros por fecha/folio y cruce oficial con `AppMovilRegistro` + `RelacionTicketTaxista` + `remisioM`

5. Configuracion
   - uso de cuatro bases diferenciadas
   - dependencia explicita de `compuamdoPlaza`, `joyeriaPlaza`, `mkt2`, `ControlTaxis`

## Faltantes detectados originalmente en Desktop

Antes de esta integracion faltaban o estaban incompletos:

- conexiones locales listas para `REYNA / sa / 280625`
- soporte directo en el importador para `compuamdoPlaza` y `mkt2`
- espejo local del flujo `Registro App movil`
- folio control local para registros App
- detalle de multiples gafetes por registro App
- carga visual de registros App desde el espejo local/importado
- autocompletado de dejada/tipo desde la tarifa seleccionada
- validaciones de pagos/comisiones para permitir `0` sin bloquear guardado cuando la intencion es liquidar el pendiente

## Cambios integrados en esta actualizacion

### Configuracion local

- `Config/appsettings.json`
- `Config/appsettings.Development.json`

Se cambiaron cadenas y nombres de bases a:

- servidor: `REYNA`
- usuario: `sa`
- password: `280625`
- bases:
  - `compuadmoPlaza`
  - `joyeriaPlaza`
  - `mkt2`
  - `ControlTaxis`

### Importador / migracion

- `ControlTaxiDesktop.Tools/Program.cs`

Se agrego:

- lectura automatica de configuracion local desde `Config/appsettings.json`
- defaults para `import-sqlserver`
- soporte correcto del alias `compuamdoPlaza -> compuadmo`
- ejemplo/documentacion actualizada con las 4 bases
- correccion del nombre real `compuadmoPlaza`

### Registro App movil en Desktop

- `ControlTaxiDesktop/Models/LocalOperationsModels.cs`
- `ControlTaxiDesktop/Services/LocalOperationsRepository.cs`
- `ControlTaxiDesktop/OperationsWindow.xaml`
- `ControlTaxiDesktop/OperationsWindow.xaml.cs`

Se agrego:

- `LocalAppRecordInput`
- `LocalAppRecordRow`
- carga de grid desde `mkt__dbo__AppMovilRegistro`
- guardado local enriquecido de `Registro App movil`
- tablas espejo locales:
  - `mkt__dbo__AppMovilFolioControl`
  - `mkt__dbo__AppMovilRegistro`
  - `mkt__dbo__AppMovilRegistroGafetes`
- generacion de `FolioControl`
- almacenamiento de multiples gafetes
- actualizacion sincronizada de `LocalRegistros`
- autollenado de `dejada` y `tipo transporte` al elegir tarifa

### Pagos y comisiones con valor 0

- `ControlTaxiDesktop/Services/LocalPosRepository.cs`

Se ajusto:

- `RegisterPaymentAsync`
  - ahora permite `0` como valor valido
  - si el usuario captura `0`, Desktop aplica automaticamente el saldo pendiente completo
  - se mantiene el bloqueo solo para importes negativos o importes mayores al saldo
- `PayCommissionAsync`
  - ahora permite `0` como valor valido
  - si el usuario captura `0`, Desktop liquida automaticamente el saldo completo de la comision
  - se mantiene el bloqueo solo para importes negativos o mayores al saldo
- auditoria
  - se registra el importe realmente aplicado
  - se marca `AUTO-PENDIENTE` o `AUTO-SALDO` cuando el valor capturado fue `0`

### Comisiones y relacion ticket-taxista

- `ControlTaxiDesktop/ControlTaxiDesktop.csproj`
- `ControlTaxiDesktop/Services/LocalSqlServerSource.cs`
- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
- `ControlTaxiDesktop/Services/LocalOperationsRepository.cs`
- `ControlTaxiDesktop/OperationsWindow.xaml`
- `ControlTaxiDesktop/OperationsWindow.xaml.cs`
- `ControlTaxiDesktop/Models/LocalOperationsModels.cs`

Se agrego o ajusto:

- acceso directo a SQL Server desde Desktop para consultar `mkt2`, `compuadmoPlaza` y `joyeriaPlaza`
- calculo de comisiones con tickets reales de `remisioM` y no solo con `mov_operacion`
- reglas autoritativas:
  - `Turibus Salmoran = 20%`
  - `Majestic / Travel Experience = 8%`
  - `VANTR / Van Transportadora = 10%`
  - resto de taxi/van/uber/turibus ado = `10%`
- exclusion de tickets `BF*` en Salmoran/Turibus
- aplicacion de la dejada al ticket con venta mas alta
- lectura de pagos por ticket desde `pagosM` + `monedas`
- lectura de egresos/gastos por ticket para descontar degustacion, gastos varios, bebidas, reparaciones y cajas de regalo como en Web
- estado de comision en relaciones basado en `pago_comision` y no en pago/dejada
- filtros de fecha y busqueda en la vista Desktop de relaciones
- visualizacion de `Pago com.` y `Pagado dejada` por separado en la vista
- cruce oficial por:
  - `AppMovilRegistro.folio_app`
  - `RelacionTicketTaxista.FolioOperacion`
  - `compuadmoPlaza.dbo.remisioM.folioregistro`
  - `joyeriaPlaza.dbo.remisioM.folio_registro`

## Archivos modificados

- `Config/appsettings.json`
- `Config/appsettings.Development.json`
- `ControlTaxiDesktop.Tools/Program.cs`
- `ControlTaxiDesktop/Models/LocalOperationsModels.cs`
- `ControlTaxiDesktop/ControlTaxiDesktop.csproj`
- `ControlTaxiDesktop/Services/LocalSqlServerSource.cs`
- `ControlTaxiDesktop/Services/LocalOperationsRepository.cs`
- `ControlTaxiDesktop/Services/LocalPosRepository.cs`
- `ControlTaxiDesktop/OperationsWindow.xaml`
- `ControlTaxiDesktop/OperationsWindow.xaml.cs`

## Archivos agregados

- `Documentacion/REPORTE_SINCRONIZACION_WEB_DESKTOP_20260703.md`
- `ControlTaxiDesktop/Services/LocalSqlServerSource.cs`

## Cambios que corresponden a actualizaciones entre 29 de junio y 2 de julio

Por revision del ZIP/documentacion/codigo fuente, estos puntos corresponden a la ola mas reciente:

- refinamiento de `Registro App movil`
- ajustes en relaciones y pago de dejadas
- reglas nuevas de comisiones
- cruce de tickets reales y deducciones operativas por ticket
- validaciones para aceptar `0` en pagos/abonos cuando corresponda liquidar pendiente
- cambios de texto/columnas en reportes
- consolidacion de configuracion multibase

## Estado de sincronizacion

Quedo sincronizada en esta ronda la parte de configuracion local, importacion local, `Registro App movil`, relaciones y la logica local de pagos/comisiones que debia aceptar `0` como en la version Web mas reciente revisada.

Las pantallas principales `Portal`, `Pos`, `Relaciones`, `Comisiones`, `Cortes`, `Reportes`, `Usuarios`, `Gafetes`, `Catalogos` y exportaciones ya estaban presentes en Desktop y esta actualizacion se enfoco en los huecos reales detectados en la comparacion mas reciente, especialmente en app movil, cruces con tickets reales, deducciones operativas, comisiones y validaciones de pagos/comisiones.

## Validacion tecnica

Compilaciones correctas:

- `ControlTaxiDesktop`
- `ControlTaxiDesktop.Tools`

Validaciones dirigidas:

- `1110` -> venta `9462`, comision `1489`
- `1163` -> venta `3190`, relacion recalculada con tickets reales
- `1179` -> venta `926`; se mantiene documentado como dependiente de datos fuente locales
- `1182` -> venta `108900`
- `63617` -> transporte `VANTR`; regla Desktop queda en `10%` para el recalculo autoritativo
