using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>Maps OperationType to reference_data.operation_types (schema.md).</summary>
public sealed class OperationTypeConfiguration : IEntityTypeConfiguration<OperationType>
{
    public void Configure(EntityTypeBuilder<OperationType> builder)
    {
        builder.ToTable("operation_types");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new OperationTypeId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

        builder.Property(x => x.BehaviorKindId)
            .HasConversion(id => id.Value, value => new OperationBehaviorKindId(value))
            .HasColumnName("behavior_kind_id")
            .IsRequired();

        builder.HasOne<OperationBehaviorKind>()
            .WithMany()
            .HasForeignKey(x => x.BehaviorKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.Name)
            .HasDatabaseName("ux_operation_types_active_name")
            .IsUnique()
            .HasFilter("is_active");

        builder.HasIndex(x => x.BehaviorKindId).HasDatabaseName("ix_operation_types_behavior_kind");
    }
}
