# Entrega final Desktop

Fecha: 2026-06-29.

## Resumen

El proyecto Desktop queda preparado para entrega, prueba piloto y mantenimiento. No se elimino el sistema Web y no se modifico la logica de negocio validada.

## Funcionalidades implementadas

- Aplicacion WPF nativa para Windows.
- SQLite local en `DatosLocal\ControlTaxi.db`.
- Creacion automatica de carpeta `DatosLocal`.
- Creacion automatica de base SQLite si no existe.
- Login local y permisos.
- Ventas.
- Pagos.
- Comisiones.
- Cortes.
- Registros.
- Tarifas.
- Hoteles.
- Taxistas.
- Gafetes.
- Transportes.
- Guias.
- Gastos.
- Relaciones.
- Reportes especializados.
- Exportaciones Excel/PDF/texto.
- Ticket de dejada imprimible.
- Auditoria local.
- Importador y pipeline de validacion.

## Funcionalidades validadas

Con datos reales importados:

- Ventas.
- Pagos.
- Comisiones.
- Cortes.
- Usuarios/permisos.
- Auditoria.
- Gafetes por conteo/datos.
- Pipeline `normalize-pos`.
- Pipeline `validate-parity`.
- Publicacion Windows x64.
- `ControlTaxiDesktop.exe --self-test`.

## Requisitos del sistema

- Windows 10/11 x64.
- .NET Desktop Runtime compatible con `net9.0-windows`, salvo publicacion self-contained futura.
- Permisos de escritura en la carpeta de la aplicacion.
- Espacio suficiente para `DatosLocal\ControlTaxi.db` y respaldos.

## Dependencias

Desktop:

- WPF / .NET.
- `Microsoft.Data.Sqlite`.
- SQLite nativo incluido por paquetes de publicacion.

Herramientas:

- .NET SDK para ejecutar `ControlTaxiDesktop.Tools`.
- SQL Server local solo cuando se importen datos desde SQL Server.

## Procedimiento de instalacion

1. Publicar:

   ```powershell
   dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
   ```

2. Copiar la carpeta:

   ```text
   ControlTaxiDesktop\bin\Release\net9.0-windows\win-x64\publish
   ```

3. Pegar en la PC destino.
4. Colocar, si existe, `DatosLocal\ControlTaxi.db`.
5. Ejecutar `ControlTaxiDesktop.exe`.

## Procedimiento para respaldo

1. Cerrar la aplicacion.
2. Copiar `DatosLocal\ControlTaxi.db`.
3. Guardar con fecha en carpeta de respaldos.

Ver detalle en `BACKUP_Y_RECUPERACION.md`.

## Procedimiento para restauracion

1. Cerrar la aplicacion.
2. Respaldar la base actual.
3. Copiar el respaldo elegido como `DatosLocal\ControlTaxi.db`.
4. Abrir la aplicacion.
5. Validar login y reportes.

## Riesgos conocidos

- La comparacion visual exacta de Excel/PDF contra Web requiere revision manual.
- La impresion fisica depende del driver, papel y margenes de la impresora real.
- Registro App movil online no aplica en modo 100% offline; Desktop trabaja con datos importados.
- `Guias` queda parcial si no se entrega fuente real util.
- Si se publica con `--self-contained false`, la PC necesita runtime .NET.

## Recomendaciones de mantenimiento

- Respaldar diariamente la base SQLite.
- Ejecutar `validate-parity` despues de cada importacion real.
- No editar SQLite manualmente.
- No cambiar reglas de negocio sin comparar contra Web.
- Mantener el sistema Web como referencia hasta retiro formal.
- Probar `ControlTaxiDesktop.exe --self-test` despues de cada entrega.

## Cambios de calidad realizados

- Se ajusto la creacion normal de `DatosLocal\ControlTaxi.db`.
- `ControlTaxi.prueba.db` queda reservado para self-test.
- Se agregaron PRAGMAs SQLite para estabilidad y rendimiento.
- Se redujeron consultas repetidas en carga de operaciones.
- Se agrego registro de errores UI en auditoria local.
- Se generaron manuales tecnicos, instalacion, usuario, backup y arquitectura.

## Revision de calidad ejecutada

| Area | Resultado |
|---|---|
| Codigo muerto | No se elimino codigo fuente porque no se identifico una referencia 100% segura para retirar sin riesgo funcional. |
| Temporales/build | Se ejecuto `dotnet clean 'CONTROL TAXI.sln'` para limpiar artefactos generados de compilacion. |
| Recursos | Conexiones SQLite y lectores usan `using` / `await using`. |
| Consultas repetidas | Se redujeron lecturas duplicadas de catalogos en carga de `OperationsWindow`. |
| SQLite | Se agrego `busy_timeout`, WAL y `synchronous=NORMAL`. |
| Errores UI | Se agrego `LocalErrorLogger` para registrar errores importantes en `LocalAuditoria`. |
| Seguridad | No se encontro contrasena real dura en Desktop. `ControlTaxiDesktop.Tools` recibe contrasena por parametro solo durante importacion local. |
| Red | No se encontraron llamadas de red en `ControlTaxiDesktop`; las coincidencias `http://` son namespaces XAML/OpenXML. |
| Rutas | La aplicacion usa rutas relativas a la carpeta del ejecutable y crea `DatosLocal` automaticamente. |

## Validacion final ejecutada

```powershell
dotnet clean 'CONTROL TAXI.sln'
dotnet build 'CONTROL TAXI.sln' --no-restore
dotnet run --project ControlTaxiDesktop\ControlTaxiDesktop.csproj -- --self-test
dotnet run --project ControlTaxiDesktop.Tools -- validate-parity --db .\DatosLocal\ControlTaxi.db
dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline --db .\DatosLocal\ControlTaxi.db
dotnet publish ControlTaxiDesktop\ControlTaxiDesktop.csproj -c Release -r win-x64 --self-contained false
ControlTaxiDesktop.exe --self-test
```

Resultado:

- Limpieza: correcta.
- Build: correcto, 0 errores.
- Self-test por proyecto: correcto.
- Validacion de paridad: correcta.
- Pipeline: correcto.
- Publicacion x64: correcta.
- Self-test del ejecutable publicado: correcto.
- Self-test desde otro directorio de trabajo: correcto.
