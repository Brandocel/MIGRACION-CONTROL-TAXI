# Contexto de trabajo — 18 de septiembre de 2026

Instalación del escritorio en **Casco Viejo** con el paquete `PLAZA28-DESKTOP-20260917.zip`.
Quedó funcionando: arranca en modo Casco, encuentra su credencial y el usuario Guadalupe entra.

Continuación de `CONTEXTO-SESION-2026-09-10.md`.

---

## 1. Lo que hay que saber para instalar en Casco (no es igual que Plaza 28)

### 1.1 La carpeta decide la sucursal

`App.xaml.cs`, `ShouldStartCascoServices()`, decide el modo por la **ruta de instalación**:

- Si contiene `DESKTOP TAXIS`, `PLAZA28`, `PLAZA 28`, `RELEASE-CONTROLTAXI-PLAZA28-PRODUCCION` o
  `SYNCTAXI_PLAZA28` → modo **Plaza 28**. Esta revisión va **primero** y gana.
- Si contiene `CASCO VIEJO`, `CASCO NUEVO` o `RELEASE-CONTROLTAXI-CASCO-PRODUCCION` → modo **Casco**.
- Se puede forzar con la variable de entorno `CONTROL_TAXI_START_CASCO_SERVICES=true`.

**La trampa:** seguir la guía de Plaza 28 en Casco (descomprimir en `DESKTOP TAXIS`) deja el
programa corriendo como Plaza 28 sin avisar. En Casco nunca se usa esa carpeta.

### 1.2 Credencial

Archivo aparte: `Config\casco.credentials.dat`, DPAPI `LocalMachine`, entropía
`ControlTaxiDesktop.Casco.Credentials.v1`. Si falta, el propio escritorio abre la ventana de
conexión de Casco al arrancar — no hay script que correr.

`CascoCredentialStore` la busca también en la carpeta **padre** del ejecutable. Por eso en la
instalación de Casco, donde el ejecutable vive en `...\Desktop\` y la credencial en `...\Config\`,
se puede actualizar en el mismo lugar sin volver a capturarla.

### 1.3 Sincronizador

En Casco **no es tarea programada**: corre dentro del escritorio (`CascoBackgroundSyncService`)
mientras está abierto. **No se instala `INSTALAR-SINCRONIZADOR-PLAZA28`** en esa máquina.

Verificado tras instalar: sólo corre el proceso del escritorio. Los watchers viejos de julio
(`ControlTaxiDesktop.Tools.exe casco-badge-sync-watch` / `casco-auto-sync-watch`) **no**
arrancaron, aunque la carpeta `Tools\` de julio sigue en la instalación.

---

## 2. La máquina de Casco

| | |
|---|---|
| Instalación | `C:\Users\USUARIO2\Documents\CASCO NUEVO\Release-ControlTaxi-Casco-Produccion\Desktop\` |
| Credencial | `C:\Users\USUARIO2\Documents\CASCO NUEVO\Release-ControlTaxi-Casco-Produccion\Config\casco.credentials.dat` |
| Respaldo previo | `...\Release-ControlTaxi-Casco-Produccion-RESPALDO-20260918` (con credencial) |
| Acceso directo | `CASCO VIEJO SISTEMA TAXIS` (se conservó) |
| SQL Server | `192.168.1.70,50807`, base `mkt` — la máquina llega (`TcpTestSucceeded True`) |
| Tareas programadas de Casco | ninguna |

Se actualizó en el mismo lugar, descomprimiendo el zip dentro de `...\Desktop`.

---

## 3. Usuarios de Casco

**Casco y Plaza 28 tienen listas de usuarios separadas**, cada una en `dbo.ControlTaxiUsuarios` de
su propio SQL Server. Dar de alta a alguien en una no lo da de alta en la otra.

Antes de hoy Casco tenía **un solo usuario**: `hoka` (Administrador, CV, activo) — y con
`PuedeAutorizarComision = 0`. Nadie sabe su contraseña.

Se creó **Guadalupe** directamente en SQL: Administrador, sucursal `CV`, activa, todos los
permisos **incluido autorizar comisiones**. Contraseña cifrada con el mismo formato que usa el
programa (`LocalUserRepository.HashPassword`): `PBKDF2$100000$<salt b64>$<hash b64>`, SHA-256,
sal de 16 bytes, clave de 32. La contraseña la escribió el usuario en pantalla oculta; no quedó en
ningún archivo.

Con esto ya hay alguien en Casco que puede **autorizar** pagos de comisión (antes nadie podía).

---

## 4. Estado de datos de Casco

- `/casco-api/health` responde `{"ok":true,"mysql":true,"branchCode":"CV"}`.
- La API de Casco tiene **31 registros en total, todos de julio**; el último, folio `0039`,
  el 27/07/2026. **No hay capturas desde entonces.** Para probar el sincronizador hay que hacer
  una captura de prueba desde la tablet de Casco con el escritorio abierto.
- La sucursal CV tiene `IsReadOnly = true`: el panel de captura manual de *Registro App móvil*
  sale desactivado a propósito (en Casco los registros entran desde la tablet).
- `/casco-api/` **no** tiene los endpoints de camiones ni el de `/sync/push-cuadre`. El cuadre se
  exporta bien, pero la publicación a Hoka falla con aviso (pendiente desde el 09/09).

---

## 5. Pendientes

1. **Probar el sincronizador de Casco**: captura de prueba desde la tablet con el escritorio
   abierto; debe aparecer en *Registro App móvil*.
2. Revisar que Casco muestre sus ventas y comisiones.
3. Decidir si a `hoka` se le prende `PuedeAutorizarComision` o se deja sólo con Guadalupe.
4. Parchear `/casco-api/` con `/sync/push-cuadre` (pendiente del 09/09).
5. Evaluar quitar la carpeta `Tools\` de julio de la instalación de Casco: hoy no arranca, pero si
   algún día se instalara la tarea `instalar-sincronizador-casco.ps1`, correrían dos
   sincronizadores sobre la misma base.
