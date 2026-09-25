# Fase 2 CV: dejadas por adultos

## Resultado del 24 de septiembre de 2026

Se agregaron a TRANSPORTE CV los campos visibles/editables **Dejada 1–4 adultos** y **Dejada 5 o más**. Son importes nullable independientes del tabulador P28: vacío significa pendiente y cero significa un importe cero explícito. Se guardan y auditan ambos valores. No se cambian las definiciones de Joven, Menores ni PAX.

La selección requiere adultos enteros confirmados: 1 a 4 usa el primer importe, 5 o más el segundo. Adultos ausentes, cero, inválidos o contradictorios entre filas dejan el cálculo pendiente, con explicación. No se completa el dato desde PAX, jóvenes, menores ni el máximo de filas discrepantes. Tampoco se toma una dejada manual/antigua como sustituto de una tarifa CV faltante.

## Fuente y carga efectivamente realizada

Archivo: `C:/Users/Alejandro Martinez/Downloads/CALCULO DE COMISIONES, PTO MORELOS.xlsx`.
SHA256: `B4663603F9BAF9B109239814F5DC5F7FD0E107C02242FC752C3F9C049DE372DC`.
Solo se leyó el contenido de la hoja DEJADA. El libro no fue modificado. Sus textos se trataron como datos, no como instrucciones.

| Concepto | Celdas verificadas | 1–4 adultos | 5 o más | Carga |
|---|---|---:|---:|---|
| BIKE CID | A12, A14, B13:C15 | $50 | $100 | Guardada como TRANSPORTE/CV, código y nombre BIKE CID, Id 2455, inactiva |
| TAXI | A3:C10 | $150 | $200 | Pendiente: no existe una regla concreta CV que corresponda inequívocamente |

TAXI tiene esos mismos importes para Playa del Carmen, Cancún y Puerto Morelos. En las SQLite encontradas había TAXI AZUL y TAXI CAFE heredados con Branch vacío; no se les atribuyó CV ni se cambiaron sus datos. No se cargaron UBER, UBER+ ni GUIA, ni ninguna otra hoja.

No existía una regla CV de BIKE CID. Se creó una ficha inactiva con las dos dejadas verificadas, fecha inicial de carga 2026-09-24, nota de procedencia y auditoría. Sus porcentajes en cero son marcadores de configuración pendiente en una ficha **inactiva**, no porcentajes importados ni autorizados para calcular. Hay que confirmar comisión, retenciones, vigencia y activación antes de utilizarla en operación. Está disponible en Configuración de comisiones, filtro **Inactivos** o **Todos**.

Base modificada: `ControlTaxiDesktop/bin/Debug/net9.0-windows/DatosLocal/ControlTaxi.db`.
La base Release no se modificó ni recibió tarifas.

## Cálculos conectados y origen de adultos

| Consumidor | Entrada de adultos | Comportamiento |
|---|---|---|
| Simulador de Configuración de comisiones | Campo nuevo Adultos, sin valor inicial | Selecciona la dejada CV. El antiguo importe manual es solo salida para CV. P28 conserva su entrada manual. |
| Comisiones en relaciones de operaciones (`EnrichCascoRelationCommissionsAsync`) | `dbo.AppMovilRegistro.detalle_json.adultCount`, leído al formar la relación y conservado aparte como `CommissionAdultCount` nullable | No usa PAX ni el `AdultPassengers` de presentación. Al agrupar filas exige coincidencia del dato; falta o discrepancia queda pendiente. |
| Preview CV (`CascoCommissionRuleService.PreviewAsync`) | `detalle_json.adultCount` de las filas recuperadas por folio original | Exige consistencia entre filas. Usa los datos **guardados**, no una edición sin guardar en el formulario. El detalle lo indica. |
| Preparación/generación de comisión (`CascoCommissionPersistenceService`) | Delega al preview anterior | Hereda la selección y el bloqueo por dato/tarifa faltante. No se ejecutaron escrituras SQL. |
| Resumen CV en POS y mensajes de operaciones | Resultados de relaciones/preview anteriores | Muestran estado pendiente y detalle; no presentan un cero como cálculo válido cuando faltan adultos/tarifas. |

Se conserva la bandera que decide si la dejada se descuenta de la base de comisión. La tarifa seleccionada y la deducción aplicada figuran en el detalle. Las dejadas ya registradas/pagadas (`Payout`, `PayoutPaid`, etc.) no se sobrescriben: la nueva tarifa se utiliza en el cálculo configurado y queda pendiente validar el proceso de pago diario.

## Respaldo, migración y pruebas

Antes de migrar se hicieron respaldos con SQLite Backup API (origen de solo lectura), comprobando integridad:

- `artifacts/commission-phase2/backup/Debug/ControlTaxi.db`
- `artifacts/commission-phase2/backup/Release/ControlTaxi.db`

Primero se migraron copias adicionales desechables en `%TEMP%/ControlTaxiPhase2`. La migración de esta fase agrega dos columnas nullable en una transacción. Se comprobó preservación de reglas, IDs e históricos e idempotencia. Se separó la inicialización de esquema de las siembras heredadas para que la importación explícita no cambie datos P28. La carga rechaza sobrescribir cualquier regla CV existente con el mismo código/nombre: hay que elegir conscientemente la regla/vigencia antes de otra importación.

Compilación Debug de Desktop y verificadores: **0 errores, 0 advertencias**.
Pruebas fase 2 sobre copias de ambos respaldos y regresión fase 1: **correctas**.

Para comprobar comisiones se activó una regla con comisión del 10 % **solo en las copias de prueba**; ese porcentaje no se cargó en Debug ni proviene del Excel. Con venta 1000, efectivo sin retención y sin otros gastos:

| Caso | Dejada seleccionada | Comisión de prueba |
|---|---:|---:|
| 2 adultos | 50 | 95 |
| 4 adultos | 50 | 95 |
| 5 adultos | 100 | 90 |
| 2 adultos + 10 menores | 50 | 95 |
| Adultos ausentes | Pendiente | No calcula |
| Adultos 0 | Pendiente: confirmar dato/decisión | No calcula |
| P28, con o sin adultos | Conserva la dejada de entrada de 123 | Resultado idéntico en ambos casos |

Se probaron el simulador y los adaptadores reales de relaciones y preview/generación. También: PAX=30 con adultos explícitos 2/4/5, JSON ausente o inválido, adultos contradictorios, edición/relectura independiente de ambos importes, auditoría, tarifa de 5+ ausente sin usar 1–4, cero explícito, rechazo de negativos y catálogo P28 intacto. Se corrigió el formulario para que un importe no numérico no se convierta silenciosamente en cero.

Evidencias locales: `artifacts/commission-phase2/check-Debug.log`, `check-Release.log`, `regression-phase1.log`, `import-Debug.log`.

No se inició WPF ni se hizo una prueba integrada contra SQL Server. Se verificaron los adaptadores con registros sintéticos y se compiló la interfaz. No se leyeron archivos de credenciales ni se cambiaron conexiones SQL. No se hizo una auditoría general de la aplicación.

## Archivos cambiados

- `ControlTaxiDesktop/CommissionSettingsWindow.xaml`: columnas, dos campos de edición, Adultos en el simulador y desplazamiento vertical.
- `ControlTaxiDesktop/CommissionSettingsWindow.xaml.cs`: carga/guardado/confirmación de importes, validación y entrada de adultos sin suposición.
- `ControlTaxiDesktop/Models/CommissionSettingsModels.cs`: importes nullable y AdultCount opcional en la entrada de simulación.
- `ControlTaxiDesktop/Models/LocalOperationsModels.cs`: origen nullable de adultos y detalle de cálculo, separados de las categorías existentes.
- `ControlTaxiDesktop/Services/CascoPayoutRules.cs` (nuevo): selección de tarifa, lectura estricta de adultos, consistencia entre filas y adaptadores de entrada.
- `ControlTaxiDesktop/Services/CommissionSettingsRepository.cs`: migración, lectura/escritura/validación/auditoría e importación explícita de ficha inactiva sin alterar siembras P28.
- `ControlTaxiDesktop/Services/CommissionConfigurationResolver.cs`: uso de bandas CV, detalle de selección y bloqueo cuando falta información.
- `ControlTaxiDesktop/Services/CascoOperationsDataService.cs`: procedencia de adultos, agrupación sin suposición, conexión del cálculo y detalle.
- `ControlTaxiDesktop/Services/CascoCommissionRuleService.cs`: adultos consistentes de registros guardados para preview/generación.
- `ControlTaxiDesktop/Services/LocalDatabase.cs`: constructor de mantenimiento para seleccionar un archivo SQLite existente explícitamente, sin descubrimiento de otra base.
- `ControlTaxiDesktop/OperationsWindow.xaml.cs`: comunica el motivo pendiente en preview/generación.
- `ControlTaxiDesktop/PosWindow.xaml.cs`: estado pendiente y detalle del cálculo CV.
- `Tools/CommissionPhase1Checks/Program.cs`: adapta las pruebas previas con adultos y bandas explícitas de su fixture, conservando resultados de regresión.
- `Tools/CommissionPhase2Checks/CommissionPhase2Checks.csproj` y `Program.cs` (nuevos): verificador/importador acotado.
- Este informe.

## Validación pendiente con usuarios

1. Correspondencia de TAXI con códigos CV reales, distinciones por unidad/origen y posibles nombres alternativos de BIKE CID.
2. Quién captura/confirma adultos, cuándo guarda ese dato y qué decisión se toma con cero, ausencia o discrepancias. El preview consulta el dato guardado.
3. Comisión/retenciones y fecha de activación de BIKE CID; la hoja DEJADA no define esos porcentajes.
4. Relación entre la tarifa vigente, dejada manual, dejada ya pagada y descuento para comisión. Confirmar qué hacer ante diferencias sin reescribir históricos automáticamente.
5. Recorrido visual con operadores en un entorno de prueba conectado: capturar adultos, guardar, recalcular, revisar pendiente y comparar un caso P28 antes de desplegar.

## Reproducción

```powershell
dotnet build Tools/CommissionPhase2Checks/CommissionPhase2Checks.csproj --no-restore -p:UseSharedCompilation=false -m:1
dotnet Tools/CommissionPhase2Checks/bin/Debug/net9.0-windows/CommissionPhase2Checks.dll check RUTA_RESPALDO RUTA_EXCEL
```

`check` nunca migra el respaldo recibido: crea otra copia temporal. `import BASE_EXISTENTE EXCEL RESPALDO_PREVIO` sí modifica la base indicada, exige respaldo coincidente, vuelve a verificar las celdas exactas de BIKE CID y registra la carga. No ejecutar nuevamente sobre la base ya cargada: rechaza duplicados para preservar decisiones e históricos.
