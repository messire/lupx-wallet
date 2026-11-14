using LupexWallet.Budgeting.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Budgeting.Infrastructure;

/// <summary>Maps ExpenseCategory to budgeting.expense_categories + expense_category_operation_types.</summary>
public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public const string OperationTypeUniqueIndex = "ux_expense_category_operation_types_operation_type";
    public const string ActiveNameUniqueIndex = "ux_expense_categories_active_name";

    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new ExpenseCategoryId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(ExpenseCategory.NameMaxLength).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(x => x.OperationTypeIds);

        builder.HasIndex(x => x.Name)
            .HasDatabaseName(ActiveNameUniqueIndex)
            .IsUnique()
            .HasFilter("is_active");

        builder.OwnsMany(x => x.Members, member =>
        {
            member.ToTable("expense_category_operation_types");
            member.WithOwner().HasForeignKey("ExpenseCategoryId").HasConstraintName("fk_expense_category_operation_types_category");
            member.Property<ExpenseCategoryId>("ExpenseCategoryId")
                .HasConversion(id => id.Value, value => new ExpenseCategoryId(value))
                .HasColumnName("expense_category_id");
            member.Property(x => x.OperationTypeId)
                .HasConversion(id => id.Value, value => new OperationTypeId(value))
                .HasColumnName("operation_type_id")
                .ValueGeneratedNever();
            member.HasKey("ExpenseCategoryId", nameof(ExpenseCategoryMember.OperationTypeId));

            // ADR-0010 §5: an operation type belongs to at most one category.
            member.HasIndex(x => x.OperationTypeId).HasDatabaseName(OperationTypeUniqueIndex).IsUnique();
        });

        builder.Navigation(x => x.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps Budget to budgeting.budgets + budget_category_limits.</summary>
public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public const string MonthCurrencyUniqueIndex = "ux_budgets_month_currency";

    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", table =>
            table.HasCheckConstraint("ck_budgets_month_first_day", "EXTRACT(DAY FROM month) = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new BudgetId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Month).HasColumnName("month").IsRequired();
        builder.Property(x => x.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(x => x.BudgetMonth);

        builder.HasIndex(x => new { x.Month, x.CurrencyId }).HasDatabaseName(MonthCurrencyUniqueIndex).IsUnique();
        builder.HasIndex(x => x.CurrencyId).HasDatabaseName("ix_budgets_currency");

        builder.OwnsMany(x => x.Limits, limit =>
        {
            limit.ToTable("budget_category_limits", table =>
                table.HasCheckConstraint("ck_budget_category_limits_limit_non_negative", "limit_amount >= 0"));
            limit.WithOwner().HasForeignKey("BudgetId").HasConstraintName("fk_budget_category_limits_budget");
            limit.Property<BudgetId>("BudgetId")
                .HasConversion(id => id.Value, value => new BudgetId(value))
                .HasColumnName("budget_id");
            limit.Property(x => x.ExpenseCategoryId)
                .HasConversion(id => id.Value, value => new ExpenseCategoryId(value))
                .HasColumnName("expense_category_id")
                .ValueGeneratedNever();
            // numeric without precision: arbitrary precision (ADR-0005).
            limit.Property(x => x.Limit).HasColumnName("limit_amount").HasColumnType("numeric").IsRequired();
            limit.HasKey("BudgetId", nameof(CategoryLimit.ExpenseCategoryId));

            limit.HasOne<ExpenseCategory>()
                .WithMany()
                .HasForeignKey(x => x.ExpenseCategoryId)
                .HasConstraintName("fk_budget_category_limits_expense_category")
                .OnDelete(DeleteBehavior.Restrict);
            limit.HasIndex(x => x.ExpenseCategoryId).HasDatabaseName("ix_budget_category_limits_category");
        });

        builder.Navigation(x => x.Limits).HasField("_limits").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
