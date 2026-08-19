using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Data;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<PosMovOperacion> MovOperaciones => Set<PosMovOperacion>();
    public DbSet<PosOperacion> Operaciones => Set<PosOperacion>();
    public DbSet<PosTransporte> Transportes => Set<PosTransporte>();
    public DbSet<PosRelacionTicketTaxista> RelacionesTicketTaxista => Set<PosRelacionTicketTaxista>();
    public DbSet<PosAppMovilFolioControl> AppMovilFolioControl => Set<PosAppMovilFolioControl>();
    public DbSet<PosGafete> Gafetes => Set<PosGafete>();
    public DbSet<PosAppMovilGafete> AppMovilGafetes => Set<PosAppMovilGafete>();
    public DbSet<PosCataxi> Taxistas => Set<PosCataxi>();
    public DbSet<PosDeptoGuia> DeptoGuias => Set<PosDeptoGuia>();
    public DbSet<PosEmpleado> Empleados => Set<PosEmpleado>();
    public DbSet<PosDejada> Dejadas => Set<PosDejada>();
    public DbSet<PosAppMovilRegistro> AppMovilRegistros => Set<PosAppMovilRegistro>();
    public DbSet<PosAuditoriaMovimiento> AuditoriaMovimientos => Set<PosAuditoriaMovimiento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PosMovOperacion>(entity =>
        {
            entity.ToTable("mov_operacion");
            entity.HasNoKey();
            entity.Property(x => x.Fecha).HasColumnName("fecha");
            entity.Property(x => x.FolioOperacion).HasColumnName("folioperacion");
            entity.Property(x => x.FolioSoluone).HasColumnName("foliosoluone");
            entity.Property(x => x.Tipo).HasColumnName("tipo");
            entity.Property(x => x.TotalEfectivo).HasColumnName("totalefectivo");
            entity.Property(x => x.TotalTarjeta).HasColumnName("totaltarjeta");
            entity.Property(x => x.Pago).HasColumnName("pago");
            entity.Property(x => x.Comision).HasColumnName("comision");
            entity.Property(x => x.FechaPago).HasColumnName("fechapago");
            entity.Property(x => x.TotalGastos).HasColumnName("totalgastos");
            entity.Property(x => x.TotalJoyeria).HasColumnName("totaljoyeria");
            entity.Property(x => x.TotalCompra).HasColumnName("totalcompra");
            entity.Property(x => x.TotalArtesania).HasColumnName("totalartesania");
            entity.Property(x => x.TotalLicor).HasColumnName("totallicor");
            entity.Property(x => x.TotalFarmacia).HasColumnName("totalfarmacia");
            entity.Property(x => x.Dejada).HasColumnName("dejada");
            entity.Property(x => x.Impuestos).HasColumnName("impuestos");
            entity.Property(x => x.TransporteTipo).HasColumnName("transportetipo");
        });

        modelBuilder.Entity<PosOperacion>(entity =>
        {
            entity.ToTable("operacion");
            entity.HasNoKey();
            entity.Property(x => x.Folio).HasColumnName("folio");
            entity.Property(x => x.Fecha).HasColumnName("fecha");
            entity.Property(x => x.Hotel).HasColumnName("hotel");
            entity.Property(x => x.StaffNombre).HasColumnName("staffnombre");
            entity.Property(x => x.IdStaff).HasColumnName("idstaff");
            entity.Property(x => x.Pax).HasColumnName("pax");
            entity.Property(x => x.Tipo).HasColumnName("tipo");
            entity.Property(x => x.TotalCompra).HasColumnName("totalcompra");
            entity.Property(x => x.TotalJoyeria).HasColumnName("totaljoyeria");
            entity.Property(x => x.TransporteTipo).HasColumnName("transportetipo");
            entity.Property(x => x.FolioSoluone).HasColumnName("foliosoluone");
        });

        modelBuilder.Entity<PosTransporte>(entity =>
        {
            entity.ToTable("transporte");
            entity.HasKey(x => x.Tipo);
            entity.Property(x => x.Tipo).HasColumnName("tipo");
            entity.Property(x => x.Nombre).HasColumnName("nombre");
            entity.Property(x => x.Moneda).HasColumnName("moneda");
            entity.Property(x => x.Impuestos).HasColumnName("impuestos");
            entity.Property(x => x.Dejada).HasColumnName("dejada");
            entity.Property(x => x.Efectivo).HasColumnName("efectivo");
            entity.Property(x => x.Tarjeta).HasColumnName("tarjeta");
            entity.Property(x => x.Amexco).HasColumnName("amexco");
            entity.Property(x => x.Comision).HasColumnName("comision");
            entity.Property(x => x.Minimo).HasColumnName("minimo");
            entity.Property(x => x.Maximo).HasColumnName("maximo");
            entity.Property(x => x.Dpto).HasColumnName("dpto");
        });

        modelBuilder.Entity<PosRelacionTicketTaxista>(entity =>
        {
            entity.ToTable("RelacionTicketTaxista");
            entity.HasNoKey();
            entity.Property(x => x.FolioApp).HasColumnName("FolioApp");
            entity.Property(x => x.FolioOperacion).HasColumnName("FolioOperacion");
            entity.Property(x => x.FolioPos).HasColumnName("FolioPos");
            entity.Property(x => x.Gafete).HasColumnName("Gafete");
            entity.Property(x => x.TaxistaId).HasColumnName("TaxistaId");
            entity.Property(x => x.TaxistaNombre).HasColumnName("TaxistaNombre");
            entity.Property(x => x.Vendedor).HasColumnName("Vendedor");
            entity.Property(x => x.TransporteTipo).HasColumnName("TransporteTipo");
            entity.Property(x => x.FechaActualizacion).HasColumnName("FechaActualizacion");
        });

        modelBuilder.Entity<PosAppMovilFolioControl>(entity =>
        {
            entity.ToTable("AppMovilFolioControl");
            entity.HasNoKey();
            entity.Property(x => x.FolioAppOriginal).HasColumnName("FolioAppOriginal");
            entity.Property(x => x.FolioControl).HasColumnName("FolioControl");
        });

        modelBuilder.Entity<PosGafete>(entity =>
        {
            entity.ToTable("gafete");
            entity.HasKey(x => x.Numero);
            entity.Property(x => x.Matricula).HasColumnName("matricula");
            entity.Property(x => x.Numero).HasColumnName("gafete");
            entity.Property(x => x.Fecha).HasColumnName("fecha");
            entity.Property(x => x.Venta).HasColumnName("venta");
            entity.Property(x => x.Hora).HasColumnName("hora");
            entity.Property(x => x.FolioOperacion).HasColumnName("folioperacion");
        });

        modelBuilder.Entity<PosAppMovilGafete>(entity =>
        {
            entity.ToTable("AppMovilGafetes");
            entity.HasNoKey();
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.FolioGafete).HasColumnName("FolioGafete");
            entity.Property(x => x.TaxistaNombre).HasColumnName("TaxistaNombre");
            entity.Property(x => x.FechaCreacion).HasColumnName("FechaCreacion");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
        });

        modelBuilder.Entity<PosCataxi>(entity =>
        {
            entity.ToTable("cataxi");
            entity.HasKey(x => x.IdTaxi);
            entity.Property(x => x.IdTaxi).HasColumnName("idtaxi");
            entity.Property(x => x.Nombre).HasColumnName("nombre");
            entity.Property(x => x.Tipo).HasColumnName("tipo");
            entity.Property(x => x.Activo).HasColumnName("activo");
            entity.Property(x => x.Telefono).HasColumnName("telefono");
        });

        modelBuilder.Entity<PosDeptoGuia>(entity =>
        {
            entity.ToTable("deptoguia");
            entity.HasKey(x => x.Matricula);
            entity.Property(x => x.Depto).HasColumnName("depto");
            entity.Property(x => x.Matricula).HasColumnName("matricula");
            entity.Property(x => x.Porcentaje).HasColumnName("porcentaje");
        });

        modelBuilder.Entity<PosEmpleado>(entity =>
        {
            entity.ToTable("empleados");
            entity.HasNoKey();
            entity.Property(x => x.CajeroId).HasColumnName("cajeroid");
            entity.Property(x => x.CajeroNombre).HasColumnName("cajeronombre");
        });

        modelBuilder.Entity<PosDejada>(entity =>
        {
            entity.ToTable("dejadas");
            entity.HasNoKey();
            entity.Property(x => x.IdTaxi).HasColumnName("idtaxi");
            entity.Property(x => x.NombreVendedor).HasColumnName("nombrevendedor");
            entity.Property(x => x.TipoTransporte).HasColumnName("tipotransporte");
            entity.Property(x => x.Unidad).HasColumnName("unidad");
            entity.Property(x => x.Telefono).HasColumnName("telefono");
            entity.Property(x => x.Fecha).HasColumnName("fecha");
        });

        modelBuilder.Entity<PosAppMovilRegistro>(entity =>
        {
            entity.ToTable("AppMovilRegistro");
            entity.HasNoKey();
            entity.Property(x => x.FolioApp).HasColumnName("folio_app");
            entity.Property(x => x.FolioPos).HasColumnName("folio_pos");
            entity.Property(x => x.IdCatalogo).HasColumnName("id_catalogo");
            entity.Property(x => x.FolioGafete).HasColumnName("folio_gafete");
            entity.Property(x => x.VendedorNombre).HasColumnName("vendedor_nombre");
            entity.Property(x => x.TipoOperacion).HasColumnName("tipo_operacion");
            entity.Property(x => x.Unidad).HasColumnName("unidad");
            entity.Property(x => x.Hotel).HasColumnName("hotel");
            entity.Property(x => x.Origen).HasColumnName("origen");
            entity.Property(x => x.Sitio).HasColumnName("sitio");
            entity.Property(x => x.Destino).HasColumnName("destino");
            entity.Property(x => x.Pax).HasColumnName("pax");
            entity.Property(x => x.Total).HasColumnName("total");
            entity.Property(x => x.Efectivo).HasColumnName("efectivo");
            entity.Property(x => x.Tarjeta).HasColumnName("tarjeta");
            entity.Property(x => x.Placas).HasColumnName("placas");
            entity.Property(x => x.ModeloVehiculo).HasColumnName("modelo_vehiculo");
            entity.Property(x => x.TelefonoTaxista).HasColumnName("telefono_taxista");
            entity.Property(x => x.Nacionalidad).HasColumnName("nacionalidad");
            entity.Property(x => x.FechaOperacion).HasColumnName("fecha_operacion");
        });

        modelBuilder.Entity<PosAuditoriaMovimiento>(entity =>
        {
            entity.ToTable("AuditoriaMovimiento");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.FechaUtc).HasColumnName("FechaUtc");
            entity.Property(x => x.Usuario).HasColumnName("Usuario");
            entity.Property(x => x.Modulo).HasColumnName("Modulo");
            entity.Property(x => x.Accion).HasColumnName("Accion");
            entity.Property(x => x.IdRegistro).HasColumnName("IdRegistro");
            entity.Property(x => x.Descripcion).HasColumnName("Descripcion");
            entity.Property(x => x.BaseDatos).HasColumnName("BaseDatos");
            entity.Property(x => x.Tabla).HasColumnName("Tabla");
            entity.Property(x => x.FolioApp).HasColumnName("FolioApp");
            entity.Property(x => x.FolioOperacion).HasColumnName("FolioOperacion");
            entity.Property(x => x.FolioPos).HasColumnName("FolioPos");
            entity.Property(x => x.Taxista).HasColumnName("Taxista");
            entity.Property(x => x.Gafete).HasColumnName("Gafete");
            entity.Property(x => x.Importe).HasColumnName("Importe");
            entity.Property(x => x.Exito).HasColumnName("Exito");
            entity.Property(x => x.Equipo).HasColumnName("Equipo");
            entity.Property(x => x.Aplicacion).HasColumnName("Aplicacion");
            entity.Property(x => x.DetalleJson).HasColumnName("DetalleJson");
        });
    }
}

public sealed class PosMovOperacion
{
    public int? FolioOperacion { get; set; }
    public DateTime? Fecha { get; set; }
    public string? FolioSoluone { get; set; }
    public string? Tipo { get; set; }
    public float? TotalEfectivo { get; set; }
    public float? TotalTarjeta { get; set; }
    public float? Pago { get; set; }
    public float? Comision { get; set; }
    public DateTime? FechaPago { get; set; }
    public float? TotalGastos { get; set; }
    public float? TotalJoyeria { get; set; }
    public float? TotalCompra { get; set; }
    public float? TotalArtesania { get; set; }
    public float? TotalLicor { get; set; }
    public float? TotalFarmacia { get; set; }
    public float? Dejada { get; set; }
    public float? Impuestos { get; set; }
    public string? TransporteTipo { get; set; }
}

public sealed class PosOperacion
{
    public int? Folio { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Hotel { get; set; }
    public string? StaffNombre { get; set; }
    public int? IdStaff { get; set; }
    public int? Pax { get; set; }
    public string? Tipo { get; set; }
    public float? TotalCompra { get; set; }
    public float? TotalJoyeria { get; set; }
    public string? TransporteTipo { get; set; }
    public string? FolioSoluone { get; set; }
}

public sealed class PosTransporte
{
    public string Tipo { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? Moneda { get; set; }
    public float? Impuestos { get; set; }
    public float? Dejada { get; set; }
    public int? Efectivo { get; set; }
    public int? Tarjeta { get; set; }
    public int? Amexco { get; set; }
    public float? Comision { get; set; }
    public float? Minimo { get; set; }
    public float? Maximo { get; set; }
    public string? Dpto { get; set; }
}

public sealed class PosRelacionTicketTaxista
{
    public string? FolioApp { get; set; }
    public string? FolioOperacion { get; set; }
    public string? FolioPos { get; set; }
    public string? Gafete { get; set; }
    public long? TaxistaId { get; set; }
    public string? TaxistaNombre { get; set; }
    public string? Vendedor { get; set; }
    public string? TransporteTipo { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}

public sealed class PosAppMovilFolioControl
{
    public string? FolioAppOriginal { get; set; }
    public string? FolioControl { get; set; }
}

public sealed class PosGafete
{
    public int? Matricula { get; set; }
    public int Numero { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Venta { get; set; }
    public DateTime? Hora { get; set; }
    public long? FolioOperacion { get; set; }
}

public sealed class PosAppMovilGafete
{
    public int? Id { get; set; }
    public string? FolioGafete { get; set; }
    public string? TaxistaNombre { get; set; }
    public DateTime? FechaCreacion { get; set; }
    public string? Estatus { get; set; }
}

public sealed class PosCataxi
{
    public long IdTaxi { get; set; }
    public string? Nombre { get; set; }
    public string? Tipo { get; set; }
    public string? Activo { get; set; }
    public string? Telefono { get; set; }
}

public sealed class PosDeptoGuia
{
    public int Depto { get; set; }
    public int Matricula { get; set; }
    public int? Porcentaje { get; set; }
}

public sealed class PosEmpleado
{
    public int? CajeroId { get; set; }
    public string? CajeroNombre { get; set; }
}

public sealed class PosDejada
{
    public int? IdTaxi { get; set; }
    public string? NombreVendedor { get; set; }
    public string? TipoTransporte { get; set; }
    public string? Unidad { get; set; }
    public string? Telefono { get; set; }
    public DateTime? Fecha { get; set; }
}

public sealed class PosAppMovilRegistro
{
    public string? FolioApp { get; set; }
    public string? FolioPos { get; set; }
    public int? IdCatalogo { get; set; }
    public string? FolioGafete { get; set; }
    public string? VendedorNombre { get; set; }
    public string? TipoOperacion { get; set; }
    public string? Unidad { get; set; }
    public string? Hotel { get; set; }
    public string? Origen { get; set; }
    public string? Sitio { get; set; }
    public string? Destino { get; set; }
    public int? Pax { get; set; }
    public decimal? Total { get; set; }
    public decimal? Efectivo { get; set; }
    public decimal? Tarjeta { get; set; }
    public string? Placas { get; set; }
    public string? ModeloVehiculo { get; set; }
    public string? TelefonoTaxista { get; set; }
    public string? Nacionalidad { get; set; }
    public DateTime? FechaOperacion { get; set; }
}

public sealed class PosAuditoriaMovimiento
{
    public long Id { get; set; }
    public DateTime FechaUtc { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string IdRegistro { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string BaseDatos { get; set; } = string.Empty;
    public string Tabla { get; set; } = string.Empty;
    public string FolioApp { get; set; } = string.Empty;
    public string FolioOperacion { get; set; } = string.Empty;
    public string FolioPos { get; set; } = string.Empty;
    public string Taxista { get; set; } = string.Empty;
    public string Gafete { get; set; } = string.Empty;
    public decimal? Importe { get; set; }
    public bool Exito { get; set; }
    public string Equipo { get; set; } = string.Empty;
    public string Aplicacion { get; set; } = string.Empty;
    public string DetalleJson { get; set; } = string.Empty;
}
