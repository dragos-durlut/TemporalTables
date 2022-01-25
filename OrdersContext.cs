using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query;

public class OrdersContext : DbContext
{
    private readonly bool _log;

    public OrdersContext()
    {
    }

    public OrdersContext(bool log)
    {
        _log = log;
    }

    public DbSet<ProductType> ProductTypes { get; set; }
    public DbSet<ProductClass> ProductClasses { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(@"Server=.;Database=Orders; Integrated Security=SSPI; Enlist = false;TrustServerCertificate=True;");

        if (_log)
        {
            optionsBuilder
                .EnableSensitiveDataLogging()
                .LogTo(Console.WriteLine, new[] { RelationalEventId.CommandExecuted });
        }

        optionsBuilder.ReplaceService<IEntityMaterializerSource, TemporalEntityMaterializerSource>();
    }

    private void SetupTemporalTableBuilder<TEntity>(TemporalTableBuilder<TEntity> temporalTableBuilder) where TEntity: class , ITemporalEntity
    {
        var periodStartTemporalPeriodPropertyBuilder = temporalTableBuilder.HasPeriodStart(TemporalEntityConstants.FromSysDateShadow);
        periodStartTemporalPeriodPropertyBuilder.HasColumnName(TemporalEntityConstants.FromSysDate);
        var periodEndTemporalPeriodPropertyBuilder = temporalTableBuilder.HasPeriodEnd(TemporalEntityConstants.ToSysDateShadow);
        periodEndTemporalPeriodPropertyBuilder.HasColumnName(TemporalEntityConstants.ToSysDate);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Product>()
            .Property(e => e.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Product>()
            .Property<int>("ProductTypeId").HasDefaultValue(12);

        modelBuilder
            .Entity<Product>()
            .HasOne(p => p.ProductType)
            .WithMany(pt => pt.Products)
            .HasForeignKey("ProductTypeId");

        modelBuilder
            .Entity<Customer>()
            .ToTable("Customers", b => b.IsTemporal(SetupTemporalTableBuilder));
        
        modelBuilder
            .Entity<Product>()
            .ToTable("Products", b => b.IsTemporal(SetupTemporalTableBuilder));

        modelBuilder
            .Entity<Order>()
            .ToTable("Orders", b => b.IsTemporal(SetupTemporalTableBuilder));

        modelBuilder.Entity<ProductType>()
            .Property<int>("ProductClassId").HasDefaultValue(112);

        modelBuilder
           .Entity<ProductType>()
           .HasOne(pt => pt.ProductClass).WithMany(pc=>pc.ProductTypes).HasForeignKey("ProductClassId");

        modelBuilder
            .Entity<ProductType>()
            .ToTable("ProductTypes");

        modelBuilder
            .Entity<ProductClass>()
            .ToTable("ProductClasses");

        
    }
}