# Backup y recuperacion

Fecha: 2026-06-29.

## Archivo critico

La informacion local vive en:

```text
DatosLocal\ControlTaxi.db
```

Ese archivo debe respaldarse.

## Respaldo manual

1. Cerrar `ControlTaxiDesktop.exe`.
2. Copiar:

   ```text
   DatosLocal\ControlTaxi.db
   ```

3. Guardar la copia con fecha, por ejemplo:

   ```text
   Backups\ControlTaxi_2026-06-29.db
   ```

## Respaldo recomendado

- Frecuencia: diario.
- Retencion sugerida:
  - 7 respaldos diarios;
  - 4 respaldos semanales;
  - 12 respaldos mensuales.
- Guardar al menos una copia fuera de la PC principal.

## Restauracion

1. Cerrar la aplicacion.
2. Renombrar la base actual:

   ```text
   DatosLocal\ControlTaxi.db
   DatosLocal\ControlTaxi_antes_restauracion.db
   ```

3. Copiar el respaldo elegido como:

   ```text
   DatosLocal\ControlTaxi.db
   ```

4. Abrir `ControlTaxiDesktop.exe`.
5. Validar reportes y login.

## Verificacion posterior

```powershell
dotnet run --project ControlTaxiDesktop.Tools -- verify --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
```

## Precauciones

- No restaurar mientras la aplicacion este abierta.
- No mezclar `ControlTaxi.prueba.db` con `ControlTaxi.db`.
- No sobrescribir la base real sin conservar copia previa.

