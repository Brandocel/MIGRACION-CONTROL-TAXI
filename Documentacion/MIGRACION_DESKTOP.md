# Migración a Control Taxi Desktop sin conexión

## Estado del análisis

La solución actual es una aplicación ASP.NET Core MVC (`ControlTaxiWeb.csproj`,
`net9.0`), no una aplicación de escritorio. Arranca Kestrel, publica rutas HTTP,
usa sesiones/cookies y presenta 20 vistas Razor. Por lo tanto, cambiar su acceso
directo por un `.exe` no cumple el objetivo: seguiría siendo un servidor web local.

El destino propuesto es una aplicación nativa de Windows en **WPF sobre .NET 9**,
sin servidor HTTP, navegador ni WebView. Las reglas de negocio se trasladarán a
una biblioteca independiente de interfaz para que las pantallas WPF las invoquen
directamente.

## Dependencias detectadas

### Datos remotos actuales

`appsettings.json` configura un SQL Server remoto y cuatro bases:

| Base actual | Uso principal |
|---|---|
| `mkt` | Operaciones POS, registros, ventas, relaciones, gafetes, comisiones y auditoría |
| `ControlTaxis` | Usuarios, permisos, pagos, cortes y datos auxiliares |
| `compuadmo` | Catálogos, ventas, productos y reportes de plaza |
| `joyeria` | Catálogos, ventas, productos y reportes de joyería |

También existe una API remota Hostinger. Su contrato actual se concentra en
`IAppTaxiApiClient` y maneja los siguientes datos:

- consulta y alta de registros móviles;
- búsqueda de taxistas;
- catálogos de hoteles y tarifas;
- actualización de dejadas pagadas e importes;
- sincronización de gafetes.

La implementación local sustituirá este contrato con `LocalAppTaxiRepository`,
que leerá y escribirá las mismas entidades dentro de la base local.

### Módulos funcionales que deben mantenerse

- Autenticación, usuarios y permisos.
- Tablero, registro diario y registro de aplicación móvil.
- Ventas, remisiones y pagos.
- Transportes, guías, taxistas y catálogos.
- Gafetes y sus devoluciones.
- Relaciones, dejadas y tickets reimprimibles.
- Gastos, cortes y comisiones.
- Exportaciones CSV, Excel y PDF, además de impresión.
- Reportes de dejadas, concentrado, cuadre, taxis y pagos/comisiones.
- Portal histórico de operaciones, productos, vendedores, catálogos y comisiones.

## Riesgos que impiden una conversión segura inmediata

1. No existe en el repositorio un respaldo de los datos de las cuatro bases ni de
   la API Hostinger. Los dos scripts de esquema incluidos son parciales y no
   contienen los datos históricos, usuarios, productos ni catálogos requeridos.
2. Los servicios contienen SQL exclusivo de SQL Server (`OBJECT_ID`, `COL_LENGTH`,
   `TOP`, `DATEADD`, `ISNULL`, esquemas `dbo` y SQL dinámico). No se puede cambiar
   el proveedor a SQLite sin portar y probar cada consulta.
3. Las pantallas existentes son Razor/HTML/JavaScript. WPF requiere controles
   nativos equivalentes; reutilizarlas implicaría WebView, expresamente excluido.

Por estas razones no se ha eliminado ni modificado código de producción durante
el análisis: hacerlo ahora perdería funcionalidad o acceso a datos.

## Plan de migración

1. **Respaldo verificable de origen.** Exportar localmente las cuatro bases SQL
   Server y el contenido que hoy devuelve la API móvil. Conservar el respaldo
   inmutable como referencia de validación.
2. **Base local.** Crear `ControlTaxi.db` SQLite y un importador idempotente de
   las copias de origen. Convertir y validar tipos, claves, índices y relaciones.
3. **Núcleo compartido.** Separar modelos, cálculos, validaciones, exportadores y
   servicios de los controladores HTTP. Reemplazar `IAppTaxiApiClient` por su
   repositorio local, sin llamadas de red.
4. **Servicios de datos.** Portar cada consulta SQL Server a EF Core/SQLite y
   comparar los resultados contra los respaldos para cada módulo.
5. **Interfaz WPF nativa.** Implementar login, navegación y cada pantalla usando
   MVVM; los formularios llaman directamente a los servicios locales. La impresión
   y exportación usarán las APIs de escritorio, sin abrir navegador.
6. **Regresión funcional.** Probar altas, edición, cálculos, permisos, reportes,
   tickets, archivos y exportaciones con escenarios de los datos exportados.
7. **Empaquetado.** Publicar `win-x64` autocontenido, un único `.exe` de inicio y
   un instalador. La base se alojará en `%ProgramData%\\ControlTaxi` para conservar
   los datos entre actualizaciones.
8. **Limpieza final.** Solo tras validar el ejecutable nativo, retirar proyectos
   web, scripts de localhost, publicaciones duplicadas y sincronizadores Hostinger.

## Criterios de aceptación

- El proceso no abre puertos, navegador ni usa `localhost`.
- Al desconectar red, todas las funciones y datos locales siguen disponibles.
- Los resultados de cada reporte y cálculo coinciden con el sistema de origen
  usando la copia de validación.
- El instalador genera un ejecutable Windows autocontenido y no requiere IIS,
  SQL Server remoto ni credenciales externas.
