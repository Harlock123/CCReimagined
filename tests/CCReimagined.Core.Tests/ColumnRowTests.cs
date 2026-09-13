using CCReimagined.App.Models;
using CCReimagined.Core.Model;

namespace CCReimagined.Core.Tests;

public sealed class ColumnRowTests
{
    private static ColumnRow Row(string native, int? maxLength, ClrTypeKind kind, int? precision = null, int? scale = null) =>
        new(new ColumnInfo
        {
            Name = "C",
            NativeTypeName = native,
            MaxLength = maxLength,
            ClrType = kind,
            Precision = precision,
            Scale = scale,
        });

    [Fact]
    public void A_declared_type_that_already_carries_its_length_is_not_doubled()
    {
        // SQLite hands back the declaration verbatim, so "VARCHAR(40)" must not become
        // "VARCHAR(40)(40)" in the grid.
        Assert.Equal("VARCHAR(40)", Row("VARCHAR(40)", 40, ClrTypeKind.String).TypeDisplay);
        Assert.Equal("DECIMAL(12,2)", Row("DECIMAL(12,2)", 12, ClrTypeKind.Decimal).TypeDisplay);
    }

    [Fact]
    public void A_bare_type_name_gets_its_length_appended()
    {
        // SQL Server, PostgreSQL and MySQL report the bare name and the length separately.
        Assert.Equal("nvarchar(40)", Row("nvarchar", 40, ClrTypeKind.String).TypeDisplay);
        Assert.Equal("varchar(max)", Row("varchar", -1, ClrTypeKind.String).TypeDisplay);
        Assert.Equal("numeric(12,2)", Row("numeric", null, ClrTypeKind.Decimal, 12, 2).TypeDisplay);
        Assert.Equal("int", Row("int", null, ClrTypeKind.Int32).TypeDisplay);
    }
}
