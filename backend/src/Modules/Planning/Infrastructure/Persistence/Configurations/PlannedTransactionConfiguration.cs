using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>Maps PlannedTransaction to planning.planned_transactions (schema.md).</summary>
public sealed class PlannedTransactionConfiguration : IEntityTypeConfiguration<PlannedTransaction>
{
    /// <summary>Shadow foreign key of both child tables.</summary>
    internal const string ForeignKey = "PlannedTransactionId";

    public void Configure(EntityTypeBuilder<PlannedTransaction> builder)
    {
        builder.ToTable("planned_transactions", table =>
        {
            table.HasCheckConstraint("ck_planned_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "ck_planned_transactions_recurrence_complete", "(recurrence_frequency IS NULL) = (recurrence_interval IS NULL)");
            table.HasCheckConstraint(
                "ck_planned_transactions_recurrence_end",
                "(recurrence_until IS NULL OR recurrence_count IS NULL) " +
                "AND (recurrence_frequency IS NOT NULL OR (recurrence_until IS NULL AND recurrence_count IS NULL))");
            table.HasCheckConstraint(
                "ck_planned_transactions_recurrence_numbers",
                "(recurrence_interval IS NULL OR recurrence_interval >= 1) AND (recurrence_count IS NULL OR recurrence_count >= 1)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new PlannedTransactionId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.OperationTypeId)
            .HasConversion(id => id.Value, value => new OperationTypeId(value))
            .HasColumnName("operation_type_id")
            .IsRequired();

        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric").IsRequired();

        builder.Property(x => x.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property(x => x.WalletId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value.Value, value => value == null ? null : new WalletId(value.Value))
            .HasColumnName("wallet_id");

        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(PlannedTransaction.DescriptionMaxLength);
        builder.Property(x => x.StartDate).HasColumnName("start_date").IsRequired();

        builder.Property(x => x.RecurrenceFrequency).HasColumnName("recurrence_frequency").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RecurrenceInterval).HasColumnName("recurrence_interval");
        builder.Property(x => x.RecurrenceUntil).HasColumnName("recurrence_until");
        builder.Property(x => x.RecurrenceCount).HasColumnName("recurrence_count");

        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");

        builder.Ignore(x => x.Recurrence);

        builder.HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_planned_transactions_created");
        builder.HasIndex(x => x.WalletId).HasDatabaseName("ix_planned_transactions_wallet").HasFilter("wallet_id IS NOT NULL");
        builder.HasIndex(x => x.OperationTypeId).HasDatabaseName("ix_planned_transactions_operation_type");
        builder.HasIndex(x => x.CurrencyId).HasDatabaseName("ix_planned_transactions_currency");

        builder.HasMany(x => x.Overrides).WithOne().HasForeignKey(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Overrides).HasField("_overrides").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Executions).WithOne().HasForeignKey(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Executions).HasField("_executions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps OccurrenceOverride to planning.occurrence_overrides; key — (plan, original occurrence date).</summary>
public sealed class OccurrenceOverrideConfiguration : IEntityTypeConfiguration<OccurrenceOverride>
{
    public void Configure(EntityTypeBuilder<OccurrenceOverride> builder)
    {
        builder.ToTable("occurrence_overrides", table =>
            table.HasCheckConstraint("ck_occurrence_overrides_amount_positive", "amount IS NULL OR amount > 0"));

        builder.Property<PlannedTransactionId>(PlannedTransactionConfiguration.ForeignKey)
            .HasConversion(id => id.Value, value => new PlannedTransactionId(value))
            .HasColumnName("planned_transaction_id");
        builder.HasKey(PlannedTransactionConfiguration.ForeignKey, nameof(OccurrenceOverride.OccurrenceDate));

        builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date").ValueGeneratedNever();
        builder.Property(x => x.IsSkipped).HasColumnName("is_skipped").IsRequired();
        builder.Property(x => x.MovedToDate).HasColumnName("moved_to_date");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric");
    }
}

/// <summary>Maps OccurrenceExecution to planning.occurrence_executions; one execution per operation.</summary>
public sealed class OccurrenceExecutionConfiguration : IEntityTypeConfiguration<OccurrenceExecution>
{
    public void Configure(EntityTypeBuilder<OccurrenceExecution> builder)
    {
        builder.ToTable("occurrence_executions", table =>
            table.HasCheckConstraint("ck_occurrence_executions_amount_positive", "amount > 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property<PlannedTransactionId>(PlannedTransactionConfiguration.ForeignKey)
            .HasConversion(id => id.Value, value => new PlannedTransactionId(value))
            .HasColumnName("planned_transaction_id");

        builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date").IsRequired();
        builder.Property(x => x.OperationId)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("operation_id")
            .IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.ExecutedAt).HasColumnName("executed_at").IsRequired();

        builder.HasIndex(PlannedTransactionConfiguration.ForeignKey, nameof(OccurrenceExecution.OccurrenceDate))
            .HasDatabaseName("ix_occurrence_executions_occurrence");
        builder.HasIndex(x => x.OperationId).IsUnique().HasDatabaseName("ux_occurrence_executions_operation");
    }
}
