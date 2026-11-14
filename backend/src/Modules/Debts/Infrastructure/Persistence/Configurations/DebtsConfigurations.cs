using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Debts.Infrastructure;

// Schema "debts" (schema.md). Ids of other modules (currency, wallet, operation) are plain uuid
// columns without FK (ADR-0001); FKs are used only inside the schema.

/// <summary>debts.creditors.</summary>
public sealed class CreditorConfiguration : IEntityTypeConfiguration<Creditor>
{
    public void Configure(EntityTypeBuilder<Creditor> builder)
    {
        builder.ToTable("creditors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new CreditorId(v)).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.Name, x.CreatedAt }).HasDatabaseName("ix_creditors_name");
    }
}

/// <summary>debts.debts with its child table debts.debt_repayments.</summary>
public sealed class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable("debts", t => t.HasCheckConstraint("ck_debts_original_amount_positive", "original_amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new DebtId(v)).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreditorId).HasConversion(id => id.Value, v => new CreditorId(v)).HasColumnName("creditor_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrencyId).HasConversion(id => id.Value, v => new CurrencyId(v)).HasColumnName("currency_id").IsRequired();
        builder.Property<decimal>("_originalAmount").HasColumnName("original_amount").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Ignore(x => x.OriginalAmount);
        builder.Ignore(x => x.IsCancelled);

        builder.HasOne<Creditor>().WithMany().HasForeignKey(x => x.CreditorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_debts_creditor");
        builder.HasIndex(x => x.CreditorId).HasDatabaseName("ix_debts_creditor");
        builder.HasIndex(x => x.CurrencyId).HasDatabaseName("ix_debts_currency");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_debts_created_at");

        builder.HasMany(x => x.Repayments).WithOne().HasForeignKey("DebtId")
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_debt_repayments_debt");
        builder.Navigation(x => x.Repayments).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class DebtRepaymentConfiguration : IEntityTypeConfiguration<DebtRepayment>
{
    public void Configure(EntityTypeBuilder<DebtRepayment> builder)
    {
        builder.ToTable("debt_repayments", t => t.HasCheckConstraint("ck_debt_repayments_amount_positive", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<DebtId>("DebtId").HasConversion(id => id.Value, v => new DebtId(v)).HasColumnName("debt_id").IsRequired();
        builder.Property(x => x.OperationId).HasConversion(id => id.Value, v => new OperationId(v)).HasColumnName("operation_id").IsRequired();
        builder.Property(x => x.WalletId).HasConversion(id => id.Value, v => new WalletId(v)).HasColumnName("wallet_id").IsRequired();
        builder.Property<decimal>("_amount").HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.RepaymentDate).HasColumnName("repayment_date").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Ignore(x => x.Amount);

        // One operation repays at most one debt or loan payment.
        builder.HasIndex(x => x.OperationId).IsUnique().HasDatabaseName("ux_debt_repayments_operation");
        builder.HasIndex("DebtId").HasDatabaseName("ix_debt_repayments_debt");
    }
}

/// <summary>debts.loans with its child tables debts.loan_schedule_items and debts.loan_payments.</summary>
public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans", t => t.HasCheckConstraint("ck_loans_amount_to_repay_positive", "amount_to_repay > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new LoanId(v)).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreditorId).HasConversion(id => id.Value, v => new CreditorId(v)).HasColumnName("creditor_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrencyId).HasConversion(id => id.Value, v => new CurrencyId(v)).HasColumnName("currency_id").IsRequired();
        builder.Property<decimal>("_amountToRepay").HasColumnName("amount_to_repay").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Ignore(x => x.AmountToRepay);
        builder.Ignore(x => x.IsCancelled);

        builder.HasOne<Creditor>().WithMany().HasForeignKey(x => x.CreditorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_loans_creditor");
        builder.HasIndex(x => x.CreditorId).HasDatabaseName("ix_loans_creditor");
        builder.HasIndex(x => x.CurrencyId).HasDatabaseName("ix_loans_currency");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_loans_created_at");

        builder.HasMany(x => x.ScheduleItems).WithOne().HasForeignKey("LoanId")
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_loan_schedule_items_loan");
        builder.Navigation(x => x.ScheduleItems).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(x => x.Payments).WithOne().HasForeignKey("LoanId")
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_loan_payments_loan");
        builder.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class LoanScheduleItemConfiguration : IEntityTypeConfiguration<LoanScheduleItem>
{
    public void Configure(EntityTypeBuilder<LoanScheduleItem> builder)
    {
        builder.ToTable("loan_schedule_items", t => t.HasCheckConstraint("ck_loan_schedule_items_amount_positive", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<LoanId>("LoanId").HasConversion(id => id.Value, v => new LoanId(v)).HasColumnName("loan_id").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired();
        builder.Property(x => x.Position).HasColumnName("position").IsRequired();
        builder.Property<decimal>("_amount").HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Ignore(x => x.Amount);
        builder.HasIndex("LoanId", nameof(LoanScheduleItem.DueDate)).HasDatabaseName("ix_loan_schedule_items_loan_date");
    }
}

public sealed class LoanPaymentConfiguration : IEntityTypeConfiguration<LoanPayment>
{
    public void Configure(EntityTypeBuilder<LoanPayment> builder)
    {
        builder.ToTable("loan_payments", t => t.HasCheckConstraint("ck_loan_payments_amount_positive", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<LoanId>("LoanId").HasConversion(id => id.Value, v => new LoanId(v)).HasColumnName("loan_id").IsRequired();
        builder.Property(x => x.OperationId).HasConversion(id => id.Value, v => new OperationId(v)).HasColumnName("operation_id").IsRequired();
        builder.Property(x => x.WalletId).HasConversion(id => id.Value, v => new WalletId(v)).HasColumnName("wallet_id").IsRequired();
        builder.Property<decimal>("_amount").HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Ignore(x => x.Amount);

        builder.HasIndex(x => x.OperationId).IsUnique().HasDatabaseName("ux_loan_payments_operation");
        builder.HasIndex("LoanId").HasDatabaseName("ix_loan_payments_loan");
    }
}
