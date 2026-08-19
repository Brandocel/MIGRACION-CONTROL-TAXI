using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppOperacionBeneficiario> PosOperacionBeneficiarios => Set<AppOperacionBeneficiario>();
    public DbSet<AppUsuario> Usuarios => Set<AppUsuario>();
    public DbSet<AppUsuarioPermiso> UsuarioPermisos => Set<AppUsuarioPermiso>();
    public DbSet<AppStaffVendedor> StaffVendedores => Set<AppStaffVendedor>();
    public DbSet<AppPago> Pagos => Set<AppPago>();
    public DbSet<AppGafete> Gafetes => Set<AppGafete>();
    public DbSet<AppGafeteAsignacion> GafeteAsignaciones => Set<AppGafeteAsignacion>();
    public DbSet<AppCorte> Cortes => Set<AppCorte>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppOperacionBeneficiario>(entity =>
        {
            entity.ToTable("PosOperacionBeneficiarios");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.FolioOperacion).HasColumnName("FolioOperacion");
            entity.Property(x => x.StaffClave).HasColumnName("StaffClave");
            entity.Property(x => x.StaffNombre).HasColumnName("StaffNombre");
            entity.Property(x => x.TransporteTipo).HasColumnName("TransporteTipo");
            entity.Property(x => x.GuiaMatricula).HasColumnName("GuiaMatricula");
            entity.Property(x => x.TaxistaId).HasColumnName("TaxistaId");
            entity.Property(x => x.TaxistaNombre).HasColumnName("TaxistaNombre");
            entity.Property(x => x.Usuario).HasColumnName("Usuario");
            entity.Property(x => x.Fecha).HasColumnName("Fecha");
        });

        modelBuilder.Entity<AppUsuario>(entity =>
        {
            entity.ToTable("Usuarios");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.Usuario).HasColumnName("Usuario");
            entity.Property(x => x.PasswordHash).HasColumnName("PasswordHash");
            entity.Property(x => x.Rol).HasColumnName("Rol");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
            entity.Property(x => x.FechaAlta).HasColumnName("FechaAlta");
        });

        modelBuilder.Entity<AppUsuarioPermiso>(entity =>
        {
            entity.ToTable("UsuarioPermisos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.Usuario).HasColumnName("Usuario");
            entity.Property(x => x.Modulo).HasColumnName("Modulo");
            entity.Property(x => x.PuedeVer).HasColumnName("PuedeVer");
        });

        modelBuilder.Entity<AppStaffVendedor>(entity =>
        {
            entity.ToTable("Staff_Vendedores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.Clave).HasColumnName("Clave");
            entity.Property(x => x.Nombre).HasColumnName("Nombre");
            entity.Property(x => x.Telefono).HasColumnName("Telefono");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
        });

        modelBuilder.Entity<AppPago>(entity =>
        {
            entity.ToTable("Pagos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.VentaId).HasColumnName("VentaId");
            entity.Property(x => x.FormaPago).HasColumnName("FormaPago");
            entity.Property(x => x.Moneda).HasColumnName("Moneda");
            entity.Property(x => x.TipoCambio).HasColumnName("TipoCambio").HasPrecision(18, 4);
            entity.Property(x => x.Importe).HasColumnName("Importe").HasPrecision(18, 2);
            entity.Property(x => x.Referencia).HasColumnName("Referencia");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
        });

        modelBuilder.Entity<AppGafete>(entity =>
        {
            entity.ToTable("Gafetes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.Numero).HasColumnName("Numero");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
        });

        modelBuilder.Entity<AppGafeteAsignacion>(entity =>
        {
            entity.ToTable("GafeteAsignacion");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.GafeteId).HasColumnName("GafeteId");
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
            entity.Property(x => x.FechaRegreso).HasColumnName("FechaRegreso");
        });

        modelBuilder.Entity<AppCorte>(entity =>
        {
            entity.ToTable("Cortes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("Id");
            entity.Property(x => x.Fecha).HasColumnName("Fecha");
            entity.Property(x => x.Usuario).HasColumnName("Usuario");
            entity.Property(x => x.Efectivo).HasColumnName("Efectivo").HasPrecision(18, 2);
            entity.Property(x => x.Tarjeta).HasColumnName("Tarjeta").HasPrecision(18, 2);
            entity.Property(x => x.Total).HasColumnName("Total").HasPrecision(18, 2);
            entity.Property(x => x.Diferencia).HasColumnName("Diferencia").HasPrecision(18, 2);
            entity.Property(x => x.Estatus).HasColumnName("Estatus");
            entity.Property(x => x.FechaCierre).HasColumnName("FechaCierre");
        });
    }
}

public sealed class AppOperacionBeneficiario
{
    public int Id { get; set; }
    public string? FolioOperacion { get; set; }
    public string? StaffClave { get; set; }
    public string? StaffNombre { get; set; }
    public string? TransporteTipo { get; set; }
    public int? GuiaMatricula { get; set; }
    public long? TaxistaId { get; set; }
    public string? TaxistaNombre { get; set; }
    public string? Usuario { get; set; }
    public DateTime? Fecha { get; set; }
}

public sealed class AppUsuario
{
    public int Id { get; set; }
    public string? Usuario { get; set; }
    public string? PasswordHash { get; set; }
    public string? Rol { get; set; }
    public string? Estatus { get; set; }
    public DateTime? FechaAlta { get; set; }
}

public sealed class AppUsuarioPermiso
{
    public int Id { get; set; }
    public string? Usuario { get; set; }
    public string? Modulo { get; set; }
    public bool PuedeVer { get; set; }
}

public sealed class AppStaffVendedor
{
    public int Id { get; set; }
    public string? Clave { get; set; }
    public string? Nombre { get; set; }
    public string? Telefono { get; set; }
    public string? Estatus { get; set; }
}

public sealed class AppPago
{
    public int Id { get; set; }
    public int VentaId { get; set; }
    public string? FormaPago { get; set; }
    public string? Moneda { get; set; }
    public decimal TipoCambio { get; set; }
    public decimal Importe { get; set; }
    public string? Referencia { get; set; }
    public string? Estatus { get; set; }
}

public sealed class AppGafete
{
    public int Id { get; set; }
    public string? Numero { get; set; }
    public string? Estatus { get; set; }
}

public sealed class AppGafeteAsignacion
{
    public int Id { get; set; }
    public int GafeteId { get; set; }
    public string? Estatus { get; set; }
    public DateTime? FechaRegreso { get; set; }
}

public sealed class AppCorte
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string? Usuario { get; set; }
    public decimal Efectivo { get; set; }
    public decimal Tarjeta { get; set; }
    public decimal Total { get; set; }
    public decimal Diferencia { get; set; }
    public string? Estatus { get; set; }
    public DateTime? FechaCierre { get; set; }
}
