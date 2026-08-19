# Pendientes finales Desktop

Fecha: 2026-06-29.

No se elimino ni retiro ningun archivo Web.

## Lo que se cerro

| Area | Cierre realizado | Evidencia |
|---|---|---|
| Registro App movil | Se localizo la logica Web/API real y se documento el flujo. Desktop usa datos importados a SQLite para consulta/reportes/ticket offline. | `PosSqlMirrorService.AppMovilSync.cs`, `PosSqlMirrorService.Relaciones.cs`, `PosController.cs`, `AUDITORIA_FINAL_MIGRACION.md`. |
| Dejadas/ticket | Se agrego generacion local de ticket de dejada desde SQLite, exportacion `.txt` y flujo de impresion. | `LocalPosRepository.BuildDejadaTicketTextAsync`, `PosWindow.xaml`, `DesktopOutputService.PrintText`. |
| Cuadre final Excel | Se agrego exportacion `.xlsx` multi-hoja con hojas equivalentes al Web. | `ExportCuadreFinalReport_Click`, `DesktopOutputService.ExportOpenXmlWorkbookAsync`. |
| Excel multi-hoja | `DesktopOutputService` ahora soporta libros OpenXML con multiples hojas. | `DesktopWorkbookSheet` y sobrecarga de exportacion multi-hoja. |
| Guias | Se reviso fuente real y se mantuvo pantalla/tabla local. | `LocalGuias`, `OperationsWindow`, normalizador desde `mkt__dbo__deptoguia`. |
| Impresion fisica | Flujo WPF preparado mediante `PrintDialog`. | `DesktopOutputService.PrintText`. |
| Build/validacion/publicacion | Se ejecuto build, self-test, validate-parity, run-pipeline, publish y self-test del `.exe`. | Comandos ejecutados correctamente el 2026-06-29. |

## Lo que sigue pendiente

| Pendiente | Motivo | Proxima accion |
|---|---|---|
| Formato visual exacto Excel Web vs Desktop | El Desktop ya exporta datos y hojas equivalentes, pero colores, anchos, estilos y formulas visuales deben compararse contra archivos Web reales. | Abrir ambos Excel y ajustar layout si el usuario requiere identidad visual exacta. |
| PDF con formato identico al Web | El Desktop genera PDF simple/offline; no se confirmo formato pixel-perfect contra Web. | Comparar archivos PDF reales y ajustar plantilla WPF/PDF. |
| Registro App movil como captura completa | La sincronizacion real Web depende de API/app movil. En Desktop offline se usan datos importados. | Decidir si se requiere captura local nueva o solo consulta offline. |
| Guias con datos reales | `mkt__dbo__deptoguia` no aporto datos utiles para poblar `LocalGuias`. | Entregar fuente real de guias o capturarlas desde Desktop. |
| Impresion con impresora real | El entorno automatico no valida driver, margenes ni corte de papel. | Probar en impresora fisica real y ajustar margenes/tamano. |

## Lo que requiere prueba manual

1. Abrir `ControlTaxiDesktop.exe` publicado.
2. Entrar con usuario real.
3. Probar permisos por modulo.
4. Exportar:
   - reporte taxi;
   - dejadas;
   - concentrado;
   - pagos/comisiones;
   - gafetes;
   - cuadre final.
5. Comparar cada Excel/PDF contra salida Web real.
6. Generar ticket de dejada con un folio real.
7. Imprimir ticket en impresora fisica.
8. Probar filtros, busquedas, botones y altas locales en:
   - ventas;
   - pagos;
   - comisiones;
   - cortes;
   - gafetes;
   - usuarios/permisos;
   - transportes;
   - guias;
   - gastos;
   - relaciones.

## Lo que no aplica offline

| Elemento Web | Por que no aplica offline |
|---|---|
| `AppTaxiApiClient` / Hostinger | Requiere red/API remota; prohibido por el objetivo Desktop 100% offline. |
| Sincronizacion viva con app movil | Requiere comunicacion con dispositivo/API. Desktop usa importacion SQLite local. |
| Recursos externos/CDN/hosting | No deben usarse en Desktop. |
| Servidor Web/IIS/localhost/Kestrel | No forman parte del Desktop WPF nativo. |

## Estado final

El Desktop queda listo para prueba piloto offline con datos reales.

No debe retirarse el Web todavia hasta terminar la prueba manual de formatos, impresora fisica y decision operativa sobre Registro App movil.
