# Verificacion de comisiones, fase 1

La herramienta no arranca WPF, no consulta SQL Server y no consulta tablas de usuarios o credenciales. `check` crea una copia SQLite desechable en `%TEMP%/ControlTaxiDesktopSelfTest/<id>/ControlTaxi.prueba.db`; nunca migra el archivo recibido.

```powershell
dotnet build Tools/CommissionPhase1Checks/CommissionPhase1Checks.csproj --no-restore -p:UseSharedCompilation=false -m:1
dotnet Tools/CommissionPhase1Checks/bin/Debug/net9.0-windows/CommissionPhase1Checks.dll backup RUTA_ORIGINAL RUTA_RESPALDO
dotnet Tools/CommissionPhase1Checks/bin/Debug/net9.0-windows/CommissionPhase1Checks.dll check RUTA_RESPALDO
```

El comando `backup` abre el origen en modo de solo lectura y utiliza la API de respaldo de SQLite. Rechaza sobrescribir el destino. Guarda los respaldos fuera de Git.

## Comprobaciones

- Migracion de reglas: conserva valores, IDs e indices; integridad SQLite; segunda ejecucion sin cambios.
- Esquemas anteriores sin Branch y migraciones parciales con Branch y unicidad anterior.
- Fallo intencional de migracion: transaccion revertida y datos originales intactos.
- Unicidad independiente por sucursal; no se atribuyen reglas heredadas a CV.
- Lista y contador CV sin catalogo fijo ni reglas heredadas; formas de pago compartidas disponibles.
- Plaza 28 mantiene exactamente HardcodedTransportCatalog antes y despues de editar CV.
- Alta y edicion CV persistidas; bloqueo de ediciones de transporte entre sucursales.
- Regla ausente, vencida, inactiva, coincidencia parcial o ambigua: no calcula ni usa respaldo.
- Porcentajes SQLite para efectivo, tarjeta y AMEX; banderas de dejada/gasto; 0 % explicito valido.
- El respaldo recibido permanece intacto.

## Revision del 24 de septiembre de 2026

Bases encontradas y respaldadas antes de cualquier migracion:

- `ControlTaxiDesktop/bin/Debug/net9.0-windows/DatosLocal/ControlTaxi.db`
- `ControlTaxiDesktop/bin/Release/net9.0-windows/DatosLocal/ControlTaxi.db`

Respaldos: `artifacts/commission-phase1-backup-20260924/Debug/ControlTaxi.db` y `artifacts/commission-phase1-backup-20260924/Release/ControlTaxi.db`. Se copiaron los archivos presentes, comprobando hashes antes y despues; no habia archivos WAL/SHM. Las pruebas usan copias adicionales mediante la API SQLite. Los originales no se migraron.

Archivos corregidos en esta revision (los demas cambios de Copilot se conservaron):

- `ControlTaxiDesktop/CommissionSettingsWindow.xaml.cs`: sucursal explicita en lista, contador, simulador y exportaciones; guardado y mensaje de configuracion faltante.
- `ControlTaxiDesktop/Models/CommissionSettingsModels.cs`: propiedad Branch en el registro sellado existente.
- `ControlTaxiDesktop/Services/CommissionSettingsRepository.cs`: seleccion CV/P28, migracion transaccional, persistencia/validacion de Branch y proteccion de CV frente a limpieza/siembra heredada.
- `ControlTaxiDesktop/Services/CommissionConfigurationResolver.cs`: coincidencia CV exacta y unica; porcentajes y deducciones de SQLite; sin respaldo para CV.
- `ControlTaxiDesktop/Services/CascoCommissionRuleService.cs`: preview de Casco usa el simulador SQLite; se elimina la lectura/migracion de reglas SQL y la formula de respaldo anterior. Los datos operativos siguen usando sus conexiones existentes.
- `ControlTaxiDesktop/Services/CascoOperationsDataService.cs`: comisiones de relaciones CV calculadas por el mismo simulador; fecha/regla faltante indicada explicitamente. No reutiliza importes antiguos como sustituto de una regla CV.
- `Tools/CommissionPhase1Checks/CommissionPhase1Checks.csproj`, `Program.cs` y este documento: verificador reproducible.

No se cargo el Excel, no se modificaron configuraciones de conexion SQL y no se leyeron archivos de credenciales. La validacion es de compilacion y servicios sobre SQLite aislado; no incluye una sesion visual WPF ni una prueba contra SQL Server.

Resultado: compilacion Debug del Desktop y del verificador correcta (0 errores, 0 advertencias). Todas las comprobaciones anteriores pasaron sobre copias de los respaldos Debug y Release. Las bases originales conservaron sus hashes.
