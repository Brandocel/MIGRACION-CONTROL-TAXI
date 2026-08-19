# Manual de instalacion Desktop

Fecha: 2026-06-29.

## Requisitos

- Windows 10/11 x64.
- .NET Desktop Runtime compatible con `net9.0-windows`, si se publica con `--self-contained false`.
- Permisos de lectura/escritura en la carpeta donde se ejecuta la aplicacion.

## Carpeta de entrega

Usar la carpeta publicada:

```text
ControlTaxiDesktop\bin\Release\net9.0-windows\win-x64\publish
```

Debe contener:

- `ControlTaxiDesktop.exe`
- DLLs de la aplicacion
- dependencias SQLite
- carpeta `DatosLocal` opcional

Si `DatosLocal` no existe, la aplicacion la crea automaticamente al iniciar.

## Instalacion manual

1. Copiar la carpeta `publish` completa a la PC destino.
2. Opcionalmente renombrarla, por ejemplo:

   ```text
   C:\ControlTaxiDesktop
   ```

3. Colocar la base real, si ya existe:

   ```text
   C:\ControlTaxiDesktop\DatosLocal\ControlTaxi.db
   ```

4. Ejecutar:

   ```text
   ControlTaxiDesktop.exe
   ```

## Primera ejecucion sin base real

Si no existe `DatosLocal\ControlTaxi.db`, el sistema crea una base vacia con estructura local.

Esto permite abrir la aplicacion, pero los datos reales deben importarse con `ControlTaxiDesktop.Tools`.

## Verificacion

Desde PowerShell en la carpeta de entrega:

```powershell
.\ControlTaxiDesktop.exe --self-test
```

Si termina sin error, la aplicacion puede crear base de prueba, autenticar usuario de prueba y ejecutar operaciones locales basicas.

## Actualizacion

1. Cerrar la aplicacion.
2. Respaldar `DatosLocal\ControlTaxi.db`.
3. Reemplazar archivos de aplicacion por la nueva carpeta publicada.
4. Conservar la carpeta `DatosLocal`.
5. Abrir `ControlTaxiDesktop.exe`.

