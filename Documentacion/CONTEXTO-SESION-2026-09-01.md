# Contexto de trabajo — 1 de septiembre de 2026

Continuación de `CONTEXTO-SESION-2026-08-28.md`. Sesión dedicada a comisiones: se cerró el caso
que quedó abierto (el 8 % de las guías), apareció y se resolvió un duplicado de venta, y se
auditó el emparejamiento unidad → regla contra todos los datos reales.

---

## 1. Los tres arreglos

Todos en `ControlTaxiDesktop\Services\LocalPosRepository.cs`. Compilan sin advertencias.

### 1.1 Comisión de guías al 8 % en vez de 10 % (`ScoreTransportCandidate`)

Reporte de operación: los folios de PANCHO RODRIGUEZ, unidad `GUIAS`, pagaban 8 %.

El catálogo del POS tiene `tipo="GUIA", nombre="GUIAS", comision=8`. Configuración de comisiones
tiene `GUIA / GUIAS, 10 %`. La unidad capturada llega como `GUIAS`, que coincide **letra por
letra** con el nombre del POS (puntaje 100) pero sólo como prefijo con el de Configuración
(puntaje 90). Como el desempate exige puntaje estrictamente mayor para voltear la prioridad, el
100 del POS le ganaba al 90 de Configuración, aunque la regla dice que en empate manda
Configuración.

Arreglo: se quita una `S` final (`StripTrailingS`, sólo en palabras de más de 3 letras) antes de
comparar. Si tras eso coinciden, cuenta como coincidencia exacta. Así singular y plural empatan y
gana Configuración, que era la intención original. Aplica a cualquier unidad con esa diferencia,
no sólo a GUIA.

### 1.2 Venta duplicada — la misma compra cobrada en dos llegadas (`ReadStoreTicketsByObservationAsync`)

Reporte: MIGUEL RADILLA, VAN VERDE 6090, 01/09/2026 — la pantalla mostraba 4 renglones y
$402.00 de comisión cuando lo correcto era 2 renglones y $201.00.

Verificado contra SQL Server de Plaza 28:

| Folio | Hora | Relación | Tickets propios | Compra |
|---|---|---|---|---|
| 5055 | 11:57 | sí (`RelacionTicketTaxista.Id` 455) | los 2 (`folioregistro` = 5055) | $2,362.50 |
| 5075 | 14:57 | **ninguna** | **ninguno** | nada |

Las dos llegadas son reales y sus dejadas de $350 son correctas. La segunda simplemente no
compró. Como no tenía relación ni tickets propios, entraba el respaldo de
`ReadStoreTicketsByObservationAsync`, que busca tickets por **nombre de taxista + fecha** en
`observaciones`. Los dos tickets dicen "MIGUEL RADILLA" y son del 01/09, así que se los asignaba
también a 5075, sin revisar que ya tenían dueño.

Arreglo: ese respaldo ahora sólo toma tickets **sin folio de llegada**
(`folioregistro` vacío o 0). El respaldo existe para el ticket que el cajero dejó sin folio y
sólo anotó el nombre; un ticket que ya trae folio pertenece a otra llegada. Como el respaldo
únicamente corre cuando la búsqueda directa por folio no encontró nada, cualquier ticket con
folio que aparezca ahí es forzosamente ajeno.

No hubo dinero pagado de más: las dos comisiones seguían pendientes cuando se detectó.

### 1.3 Códigos cortos del POS secuestrando unidades específicas (`ScoreTransportCandidate`)

Encontrado por la auditoría de la sección 2, no por reporte de operación.

La unidad capturada se guarda **cortada a 10 caracteres**, así que casi siempre es un pedazo del
nombre real. El puntaje premiaba el prefijo aunque explicara sólo la mitad de lo capturado, y los
códigos cortos y genéricos del POS le ganaban a la regla correcta:

| Capturado | Se llevaba | Debía ser | Operaciones |
|---|---|---|---|
| `TURIBUS SA` | POS "TURIBUS" → 10 % | TURIBUS SALMORAN 20 % | 112 |
| `Transporta` | POS "TRANS" / **TRANSCENDENCE** → 20 % | VAN TRANSPORTADORAS 10 % | 56 |
| `TURIBUS  S` | POS "TURIBUS" → 10 % | TURIBUS SALMORAN 20 % | 15 |

El caso de `Transporta` era el peor: emparejaba con **otra unidad** (TRANSCENDENCE) sólo por
compartir las cinco primeras letras.

Arreglo: ahora importa la dirección de la coincidencia.

- El candidato contiene toda la clave (`TURIBUS SA` dentro de `TURIBUS SALMORAN`): la regla
  explica todo lo capturado. Coincidencia fuerte, 88-97.
- La clave contiene al candidato (`TRANS` dentro de `TRANSPORTA`): la regla explica sólo un
  pedazo. Coincidencia débil, 58-88. Nunca le gana a una que explica la captura completa.

La coincidencia exacta sigue valiendo 100 y le gana a todo, así que las unidades que ya
emparejaban bien no se mueven.

---

## 2. Auditoría del emparejamiento unidad → regla

Se pasaron las **81 unidades distintas** que existen capturadas en la base (`tipo_operacion` de
`AppMovilRegistro`, `tipotransporte` de `dejadas`, `TransporteTipo` de `RelacionTicketTaxista`)
por los métodos privados reales de `LocalPosRepository` mediante reflexión — no por una copia de
la lógica — contra el catálogo real del POS (23 filas) y las reglas configuradas (17 activas).

| Resultado | Antes | Después |
|---|---|---|
| Configuración no ganaba | 3 casos (183 operaciones) | **0** |
| Sin regla (cae al 10 % por omisión) | 11 | 11 (sin cambio) |
| Usa la regla correcta | 49 | 52 |

Se comparó la salida antes y después: los tres casos rotos se corrigieron y **ningún otro
porcentaje cambió**. Ninguna unidad perdió su regla.

Las 11 sin regla son lo esperado: `S/N` (273 usos), `CALLE`, `PRIVADO`, `BESTBUS`, `Cancún Bay` y
basura de captura (`}`, `998`, `435737`, `Ç`, `0`). Caen al 10 % por omisión, que es la decisión
tomada el 28/08: preferir no emparejar antes que inventar unidad.

### 2.1 Decisión tomada: TRAVEL EXPERIENCE se queda como está

La misma unidad cobra dos tarifas según cómo se escriba, porque los dos catálogos la nombran
distinto:

- Catálogo del POS: `tipo=MAJESTIC, nombre=TRAVEL EXPERIENCE, 20 %`
- Configuración de comisiones: `MAJESTIC / MAJESTIC EXPEDITIONS, 8 %` (desde el 12/08/2026)

Resultado: capturada como "MAJESTIC" (1,159 usos) paga **8 %**; capturada como "TRAVEL
EXPERIENCE" o "TRAVEL EXP" (13 usos) paga **20 %**. También `TRAVER EXP` (10 usos, error de
dedo) se queda sin regla y paga el 10 % por omisión.

**El negocio decidió dejarlo así por ahora** (1 de septiembre). Si más adelante se quiere unificar,
la vía es un alias en `TransportLookupAliases` (el mismo mecanismo de `TAXOVERDE` y
`VANTRANSPOTADORA`) o cambiarle el nombre a la regla configurada.

---

## 3. Entorno — dónde se puede consultar qué

Detalle que costó tiempo descubrir y conviene tener presente:

- **La máquina de desarrollo no ve la base de producción.** La credencial guardada apunta a
  `.\SQLEXPRESS`, que es una **copia local atrasada** (`AppMovilRegistro` hasta el 11/08/2026,
  `compuadmo.dbo.remisioM` hasta el 20/08/2026, `dbo.mov_operacion` hasta el 15/06/2026).
  `SERVPLAZA28` no responde en el puerto 1433 desde ahí.
- Para verificar datos del día hay que consultar **en la máquina de Plaza 28** (SSMS o `sqlcmd`).
  Así se confirmó el caso de RADILLA.
- La copia local sí sirve para auditar el emparejamiento, porque el catálogo y las unidades
  históricas están completos.

---

## 4. Empaquetado y despliegue

**Paquete final: `PLAZA28-DESKTOP-20260901-3.zip`** (73.3 MB, 474 archivos), en el escritorio y en
`Release-ControlTaxi-Plaza28-Produccion\`. Trae los tres arreglos. **Falta instalarlo.**

Hay dos zips anteriores del mismo día en el escritorio (`-20260901.zip` y `-20260901-2.zip`) que
quedaron incompletos; usar sólo el `-3`.

### Cuidado al empaquetar: el publish deja la credencial dentro

`dotnet publish` sobre la carpeta de producción **arrastra `Config\plaza28.credentials.dat`** si
esa carpeta existe ahí. Pasó en el primer zip de esta sesión y se corrigió. La guía dice que el
paquete no debe llevar la credencial (es por máquina, DPAPI). Antes de comprimir, verificar:

```powershell
Get-ChildItem "Release-ControlTaxi-Plaza28-Produccion\SelfContained\Desktop\Config" -EA SilentlyContinue
```

Y después de comprimir, que el zip no traiga `credentials` ni `DatosLocal`.

### Qué revisar después de instalar

- PANCHO RODRIGUEZ / unidad GUIAS → **10 %**.
- MIGUEL RADILLA 01/09 → **2 renglones**, venta $2,362.50, comisión **$201.00**.
- Folios de TURIBUS SALMORAN capturados como `TURIBUS SA` → **20 %**.
- Folios capturados como `Transporta` → **10 %** (ya no el 20 % de TRANSCENDENCE).

Los folios anteriores se recorrigen solos: el escritorio no guarda el porcentaje, lo recalcula en
cada consulta. Un folio pagado al porcentaje viejo aparece como **PARCIAL** con su saldo.

---

## 5. Pendientes

1. **Instalar `PLAZA28-DESKTOP-20260901-3.zip`** en la máquina de prueba y luego en las de las
   chicas. Seguir `GUIA-INSTALACION-DESKTOP-MAQUINA-NUEVA.md`.
2. **Revisar los folios de TURIBUS SALMORAN y Transporta ya pagados**: 183 operaciones traían el
   porcentaje equivocado. Con el arreglo aparecerán como PARCIAL (si se pagó de menos) y habrá que
   decidir qué hacer con las que se pagaron de más.
3. Sigue pendiente todo lo de la sesión del 28/08 que no se tocó: camiones AUTOCAR / MAYA CARIBE /
   TURICUN desde Hostinger (sección 5 de ese documento), el sync que no llena
   `mkt2_pos_transports`, y la divergencia entre la API local y la del servidor.
4. **Opcional**: dejar el auditor de la sección 2 como comando de `ControlTaxiDesktop.Tools`, para
   volver a correrlo cada vez que se mueva el catálogo y ver si algo se rompió. Hoy se armó como
   herramienta temporal fuera del repositorio.

---

## 6. Reglas de negocio confirmadas en esta sesión

- La unidad capturada se guarda **cortada a 10 caracteres**; el emparejamiento tiene que asumir
  que casi siempre recibe un pedazo del nombre real.
- Un ticket de tienda que ya trae `folioregistro` **pertenece a esa llegada y a ninguna otra**.
  El respaldo por nombre de taxista sólo aplica a tickets sin folio.
- Un mismo taxista puede llegar varias veces el mismo día; cada llegada cobra su dejada, pero la
  venta se cuenta una sola vez, en la llegada donde realmente se compró.
