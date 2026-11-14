using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>Maps Fund to funds.funds (schema.md).</summary>
public sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("funds", table =>
        {
            table.HasCheckConstraint("ck_funds_target_positive", "target_amount > 0");
            table.HasCheckConstraint("ck_funds_single_link", "linked_debt_id IS NULL OR linked_loan_id IS NULL");
            table.HasCheckConstraint("ck_funds_status", "status IN ('Active', 'Closed')");
            table.HasCheckConstraint("ck_funds_closed_at", "(status = 'Closed') = (closed_at IS NOT NULL)");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, value => new FundId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.Name).HasColumnName("name").HasMaxLength(Fund.MaxNameLength).IsRequired();
        builder.Property(f => f.TargetAmount).HasColumnName("target_amount").HasColumnType("numeric").IsRequired();

        builder.Property(f => f.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property(f => f.LinkedDebtId).HasColumnName("linked_debt_id");
        builder.Property(f => f.LinkedLoanId).HasColumnName("linked_loan_id");

        builder.Property(f => f.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        // Every fund mutation (allocations included) touches UpdatedAt: optimistic check of the whole aggregate.
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").IsRequired().IsConcurrencyToken();
        builder.Property(f => f.ClosedAt).HasColumnName("closed_at");

        builder.Ignore(f => f.Target);
        builder.Ignore(f => f.Reserved);
        builder.Ignore(f => f.HasAllocationHistory);
        builder.Ignore(f => f.IsActive);

        builder.HasMany(f => f.Allocations)
            .WithOne()
            .HasForeignKey(a => a.FundId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Allocations)
            .HasField("_allocations")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(f => f.CreatedAt).HasDatabaseName("ix_funds_created_at");
        builder.HasIndex(f => f.LinkedDebtId).HasDatabaseName("ix_funds_linked_debt").HasFilter("linked_debt_id IS NOT NULL");
        builder.HasIndex(f => f.LinkedLoanId).HasDatabaseName("ix_funds_linked_loan").HasFilter("linked_loan_id IS NOT NULL");
        builder.HasIndex(f => f.CurrencyId).HasDatabaseName("ix_funds_currency");
    }
}
