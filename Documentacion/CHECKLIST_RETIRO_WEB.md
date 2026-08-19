# Checklist retiro del sistema Web

Fecha de creación: 2026-06-30.

Objetivo: usar esta lista para validar manualmente, módulo por módulo, si la aplicación Desktop WPF + SQLite ya puede reemplazar al sistema Web sin perder funcionalidad.

Regla de decisión recomendada: no retirar el Web hasta que todos los módulos aplicables estén en `Validado`, tengan evidencia y no dependan de internet, localhost, navegador, WebView, IIS, Hostinger, API remota ni servidor.

Estados permitidos:

- `Validado`: probado manualmente contra Web o contra dato real equivalente.
- `Pendiente`: falta prueba, hay diferencia o falta evidencia.
- `No aplica`: la función depende de comunicación online/API/móvil remota y fue reemplazada por alternativa local/offline documentada.

## Checklist general

| # | Módulo | Validado | Pendiente | No aplica | Pantalla abre correctamente | Botones funcionan | Filtros funcionan | Búsquedas funcionan | Totales coinciden con Web | Exportación funciona | Impresión funciona si aplica | Permisos funcionan | No depende de internet | Observaciones | Evidencia |
|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Login |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 2 | Usuarios |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 3 | Permisos |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 4 | Ventas |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 5 | Pagos |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 6 | Comisiones |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 7 | Cortes |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 8 | Gafetes |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 9 | Portal Dashboard |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 10 | Portal Operations |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 11 | Portal Commissions |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 12 | Portal Vendors |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 13 | Portal Products |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 14 | Portal Catalogs |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 15 | Portal CreateOperation |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 16 | Transportes |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 17 | Guías |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 18 | Gastos |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 19 | Relaciones |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 20 | Dejadas |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 21 | Ticket |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 22 | Reportes |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 23 | Excel |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 24 | PDF |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 25 | CSV |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 26 | Impresión |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 27 | App móvil offline/local |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 28 | Auditoría |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 29 | Catálogos |  |  |  |  |  |  |  |  |  |  |  |  |  |  |
| 30 | Backup y recuperación |  |  |  |  |  |  |  |  |  |  |  |  |  |  |

## Evidencia mínima sugerida

Para cada módulo validado conserva, cuando aplique:

- captura de pantalla Desktop;
- captura equivalente Web;
- archivo Excel/PDF/CSV generado por Desktop;
- archivo equivalente Web;
- total comparado;
- usuario usado para prueba de permisos;
- fecha/hora de prueba;
- observación de impresora usada para impresión física.

## Criterios para retirar el Web en el futuro

El Web solo debería considerarse retirable cuando:

1. todos los módulos aplicables estén `Validado`;
2. todos los módulos `No aplica` tengan justificación offline clara;
3. no exista ningún módulo `Pendiente`;
4. la prueba offline completa se haya ejecutado sin internet;
5. impresión física haya sido probada en la impresora real;
6. usuarios/permisos hayan sido probados con usuarios reales;
7. existan respaldos/restauración verificados de `DatosLocal/ControlTaxi.db`.
