using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.SharedKernel.Domain;

namespace Platform.Infrastructure.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>Maps an <see cref="Address"/> as inline columns: address_line1, address_city, …</summary>
    public static EntityTypeBuilder<T> HasAddress<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, Address?>> property)
        where T : class
    {
        builder.ComplexProperty(property, a =>
        {
            a.Property(p => p.Line1).HasMaxLength(200);
            a.Property(p => p.Line2).HasMaxLength(200);
            a.Property(p => p.City).HasMaxLength(100);
            a.Property(p => p.State).HasMaxLength(100);
            a.Property(p => p.PostalCode).HasMaxLength(20);
            a.Property(p => p.Country).HasMaxLength(2);
        });
        return builder;
    }

    /// <summary>Maps <see cref="Money"/> as {name}_amount numeric(18,2) + {name}_currency char(3).</summary>
    public static EntityTypeBuilder<T> HasMoney<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, Money?>> property)
        where T : class
    {
        builder.ComplexProperty(property, m =>
        {
            m.Property(p => p.Amount).HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnType("char(3)");
        });
        return builder;
    }

    /// <summary>Case-insensitive text column (PostgreSQL citext) — for emails, slugs, codes.</summary>
    public static PropertyBuilder<TString> IsCaseInsensitive<TString>(this PropertyBuilder<TString> builder) =>
        builder.HasColumnType("citext");

    /// <summary>Stores an enum as readable text (stable across reordering, readable in SQL).</summary>
    public static PropertyBuilder<TEnum> IsEnumText<TEnum>(this PropertyBuilder<TEnum> builder, int maxLength = 32) =>
        builder.HasConversion<string>().HasMaxLength(maxLength);
}
