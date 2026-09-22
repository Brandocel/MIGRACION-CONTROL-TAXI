namespace ControlTaxiDesktop.Models;

/// <summary>
/// El cuadre completo listo para viajar a Hostinger, con las mismas cinco secciones que tienen
/// las cinco hojas del Excel que genera <c>ExportCuadreWorkbookAsync</c>.
///
/// Existe para que Hoka Solutions muestre exactamente los mismos numeros que el Excel del
/// escritorio. Las reglas de comision (guias al 8 %, venta duplicada del mismo taxista, codigos
/// cortos del POS) viven en C# y son las que se corrigieron entre el 28/08 y el 01/09; si Hoka
/// las recalculara por su cuenta, tarde o temprano los dos reportes dirian cosas distintas.
/// Por eso el escritorio calcula una sola vez y empuja el resultado ya cuadrado.
/// </summary>
public sealed record CuadrePayload(
    string BranchCode,
    string FechaInicio,
    string FechaFin,
    string GeneratedAt,
    decimal TotalDejada,
    decimal TotalVenta,
    decimal TotalComision,
    IReadOnlyList<CuadreResumenPayloadRow> Resumen,
    IReadOnlyList<CuadreCamionPayloadRow> Camiones,
    IReadOnlyList<CuadreDejadaPayloadRow> Dejadas,
    IReadOnlyList<CuadreHotelPayloadRow> Hoteles,
    IReadOnlyList<CuadreComisionPayloadRow> Comisiones,
    IReadOnlyList<CuadreCortePayloadRow> CorteFinal);

/// <summary>Un renglon de la hoja CUADRE: TAXIS VERDES, MAJESTIC, AUTOCAR, etc.</summary>
public sealed record CuadreResumenPayloadRow(
    string Concepto,
    int Pax,
    int Adultos,
    int Jovenes,
    int Menores,
    int Entraron,
    int Salieron,
    int Unidades,
    decimal Dejada,
    decimal Comision,
    decimal Venta,
    decimal Gastos,
    decimal TicketPromedio,
    decimal PorcentajeGasto);

/// <summary>
/// El bloque "registros de camiones" que va abajo de los totales en la hoja CUADRE. Va aparte
/// del resumen porque en el Excel tambien va aparte: son las llegadas crudas de AUTOCAR, MAYA
/// CARIBE y TURICUN tal como las reporta la API, sin mezclarse con las filas de arriba.
/// </summary>
public sealed record CuadreCamionPayloadRow(
    string Concepto,
    int Pax,
    int Entraron,
    int Salieron,
    int Unidades,
    decimal Dejada);

/// <summary>Un renglon de la hoja "CUADRE dejadas" (las 25 columnas del detalle).</summary>
public sealed record CuadreDejadaPayloadRow(
    string Fecha,
    string Folio,
    string Hora,
    string Nombre,
    string Vendedor,
    string Unidad,
    string Numero,
    string Origen,
    string SitioHotel,
    int Adulto,
    int Joven,
    int Nino,
    int Pax,
    decimal ImporteDejada,
    string Telefono,
    decimal Venta,
    string EstatusDejada,
    string FechaPagoDejada,
    decimal Comision,
    decimal PagoComision,
    string EstatusComision,
    string Ticket,
    string TaxistaId,
    string Gafete,
    string Nacionalidad);

/// <summary>Un renglon de la hoja REPORTE HOTELES.</summary>
public sealed record CuadreHotelPayloadRow(
    string Hotel,
    int Pax,
    decimal Dejada,
    decimal Venta,
    decimal Comision,
    decimal Pago);

/// <summary>Un renglon de la hoja comisiones.</summary>
public sealed record CuadreComisionPayloadRow(
    string Folio,
    string Fecha,
    string Taxista,
    decimal Venta,
    decimal Comision,
    decimal Pagado,
    decimal Saldo,
    string Estatus);

/// <summary>Un renglon de la hoja CORTE FINAL.</summary>
public sealed record CuadreCortePayloadRow(
    string Fecha,
    decimal Efectivo,
    decimal Tarjeta,
    decimal Pagos,
    decimal Gastos,
    decimal Esperado,
    decimal Contado,
    decimal Diferencia,
    string Estatus);

/// <summary>Lo que responde <c>POST /sync/push-cuadre</c>.</summary>
public sealed record CuadrePushResult(
    bool Ok,
    string Mensaje,
    int FilasResumen,
    int FilasDejadas);
