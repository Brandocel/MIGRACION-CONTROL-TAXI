# Guía — instalar/actualizar el escritorio (Plaza 28) en una máquina

Pasos completos para armar el paquete y dejarlo funcionando en una máquina, de punta a punta.
Referencia rápida para cada vez que se pida "instálalo" o "pásalo a la máquina de...".

---

## 1. Compilar y armar el paquete (en la máquina de desarrollo)

Cerrar la app si está abierta (Release no compila si el .exe está corriendo):

```powershell
dotnet build "ControlTaxiDesktop/ControlTaxiDesktop.csproj" -c Release
```

Publicar self-contained directo sobre la carpeta del paquete de producción (así conserva
`appsettings.production.json` y `branches.production.json` que ya están ahí, apuntando a
SQL Server real y a la API de Hostinger — nunca a `localhost` ni credenciales locales):

```powershell
dotnet publish "ControlTaxiDesktop/ControlTaxiDesktop.csproj" -c Release -r win-x64 --self-contained true -o "Release-ControlTaxi-Plaza28-Produccion/SelfContained/Desktop"
```

Verificar que **no** haya quedado carpeta `DatosLocal` dentro de `SelfContained\Desktop`
(sobrescribiría la base local de quien instale). Si aparece, borrarla antes de empaquetar.

Armar el zip (nombre con fecha, y `-2`, `-3`... si ya se armó otro el mismo día):

```powershell
$src = "Release-ControlTaxi-Plaza28-Produccion\SelfContained\Desktop"
$dest = "Release-ControlTaxi-Plaza28-Produccion\PLAZA28-DESKTOP-AAAAMMDD.zip"
Compress-Archive -Path "$src\*" -DestinationPath $dest -CompressionLevel Optimal
```

Copiar el zip al escritorio para tenerlo a la mano.

---

## 2. Instalar en la máquina destino

Si es una máquina distinta (remota, de otra chica), estos comandos los corre el usuario
**en esa máquina**, no yo — no tengo ejecución ahí, solo veo lo que me manda por captura.

Descomprimir (cierra la app si está corriendo, y sobrescribe sin tocar `DatosLocal` porque
el zip no lo trae):

```powershell
$d=[Environment]::GetFolderPath('Desktop')
Get-Process ControlTaxiDesktop -EA SilentlyContinue | Stop-Process -Force
Expand-Archive -Path "$d\PLAZA28-DESKTOP-AAAAMMDD.zip" -DestinationPath "$d\DESKTOP TAXIS" -Force
```

Verificar que el `.exe` sí quedó ahí antes de seguir:

```powershell
Test-Path "$d\DESKTOP TAXIS\ControlTaxiDesktop.exe"
```

Si da `False`, la descompresión falló (zip incompleto, ruta con problema) — repetir.

---

## 3. Crear el acceso directo del escritorio

```powershell
$d=[Environment]::GetFolderPath('Desktop')
$exe="$d\DESKTOP TAXIS\ControlTaxiDesktop.exe"
$ws=New-Object -ComObject WScript.Shell
$sc=$ws.CreateShortcut("$d\Control Taxi Plaza 28.lnk")
$sc.TargetPath=$exe
$sc.WorkingDirectory="$d\DESKTOP TAXIS"
$sc.IconLocation=$exe
$sc.Save()
```

---

## 4. Configurar la credencial SQL (obligatorio en máquina nueva)

La credencial de conexión a SQL Server va cifrada con DPAPI, **por máquina** — el zip nunca
la trae. Sin este paso el login da: *"No se pudo conectar al servidor SQL de la sucursal
seleccionada. Revisa la configuración cifrada de conexión."*

```powershell
cd "$env:USERPROFILE\Desktop\DESKTOP TAXIS\SyncTaxi_Plaza28"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\configurar-credencial-plaza28.ps1
```

Pide la contraseña SQL de forma oculta (usuario `sa`, servidor `SERVPLAZA28\SQLEXPRESS`,
base `mkt`) — no se ve en pantalla, no se manda por chat. El script prueba la conexión antes
de guardar; si conecta, deja el archivo cifrado en `Config\plaza28.credentials.dat`.

Si PowerShell bloquea el script con `PSSecurityException` / "la ejecución de scripts está
deshabilitada", es la política de ejecución del sistema — el `-ExecutionPolicy Bypass` de
arriba lo evita sin cambiar la política global de la máquina.

---

## 5. Verificar

Abrir el acceso directo nuevo, iniciar sesión (usuario `Guadalupe`, sucursal `Plaza 28`), y
revisar lo que aplique según el cambio que se esté probando (comisiones, camiones, etc.).
