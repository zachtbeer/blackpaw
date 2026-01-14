using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Blackpaw.Data;

public class BlackpawContext : DbContext
{
    private readonly string _dbPath;

    public BlackpawContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();
    public DbSet<RunRecord> Runs => Set<RunRecord>();
    public DbSet<SystemSample> SystemSamples => Set<SystemSample>();
    public DbSet<ProcessSample> ProcessSamples => Set<ProcessSample>();
    public DbSet<DbSnapshot> DbSnapshots => Set<DbSnapshot>();
    public DbSet<Marker> Markers => Set<Marker>();
    public DbSet<DotNetRuntimeSample> DotNetRuntimeSamples => Set<DotNetRuntimeSample>();
    public DbSet<SqlDmvSample> SqlDmvSamples => Set<SqlDmvSample>();
    public DbSet<DotNetHttpSample> DotNetHttpSamples => Set<DotNetHttpSample>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchemaInfo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SchemaVersion).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.HasKey(e => e.RunId);
            entity.Property(e => e.ScenarioName).IsRequired();
            entity.Property(e => e.StartedAtUtc).IsRequired();
        });

        modelBuilder.Entity<SystemSample>(entity =>
        {
            entity.HasKey(e => e.SampleId);
            entity.Property(e => e.TimestampUtc).IsRequired();
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessSample>(entity =>
        {
            entity.HasKey(e => e.ProcessSampleId);
            entity.Property(e => e.ProcessName).IsRequired();
            entity.HasOne<SystemSample>()
                .WithMany()
                .HasForeignKey(e => e.SampleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DbSnapshot>(entity =>
        {
            entity.HasKey(e => e.DbSnapshotId);
            entity.Property(e => e.TimestampUtc).IsRequired();
            entity.Property(e => e.Label).IsRequired();
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Marker>(entity =>
        {
            entity.HasKey(e => e.MarkerId);
            entity.Property(e => e.TimestampUtc).IsRequired();
            entity.Property(e => e.MarkerType).IsRequired();
            entity.Property(e => e.Label).IsRequired();
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DotNetRuntimeSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AppName).IsRequired();
            entity.Property(e => e.ProcessName).IsRequired();
            entity.Property(e => e.RuntimeKind).IsRequired();
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SqlDmvSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimestampUtc).IsRequired();
            entity.Property(e => e.DatabaseName).HasDefaultValue("ALL");
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DotNetHttpSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimestampUtc).IsRequired();
            entity.Property(e => e.AppName).IsRequired();
            entity.Property(e => e.ProcessName).IsRequired();
            entity.Property(e => e.EndpointGroup).IsRequired();
            entity.Property(e => e.HttpMethod).HasDefaultValue("*");
            entity.HasOne<RunRecord>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
