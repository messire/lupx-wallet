using System.Text.Json;
using LupexWallet.Audit.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>Маппинг AuditEntry → audit.audit_entries (schema.md), включая CREATE RULE в миграции (см. Migrations/InitialCreate).</summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    private static readonly IReadOnlyList<AuditFieldChange> EmptyChanges = Array.Empty<AuditFieldChange>();

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries", table =>
        {
            table.HasCheckConstraint("ck_audit_entries_actor_kind", "actor_kind IN ('User', 'System')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new AuditEntryId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        // AuditActor — owned type без отдельной таблицы (table splitting): actor_kind/
        // actor_system_process остаются плоскими колонками на audit_entries (schema.md).
        builder.OwnsOne(x => x.Actor, actor =>
        {
            actor.Property(a => a.Kind)
                .HasConversion<string>()
                .HasColumnName("actor_kind")
                .HasMaxLength(16)
                .IsRequired();

            actor.Property(a => a.SystemProcessName).HasColumnName("actor_system_process").HasMaxLength(128);
        });
        builder.Navigation(x => x.Actor).IsRequired();

        var changesComparer = new ValueComparer<IReadOnlyList<AuditFieldChange>>(
            (a, b) => (a ?? EmptyChanges).SequenceEqual(b ?? EmptyChanges),
            c => (c ?? EmptyChanges).Aggregate(0, (hash, change) => HashCode.Combine(hash, change)),
            c => (IReadOnlyList<AuditFieldChange>)(c ?? EmptyChanges).ToList());

        builder.Property(x => x.Changes)
            .HasColumnName("changes")
            .HasColumnType("jsonb")
            .HasConversion(
                changes => JsonSerializer.Serialize(changes, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<AuditFieldChange>>(json, (JsonSerializerOptions?)null) ?? new List<AuditFieldChange>())
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(x => x.Changes).Metadata.SetValueComparer(changesComparer);

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt })
            .HasDatabaseName("ix_audit_entries_entity")
            .IsDescending(false, false, true);
    }
}
