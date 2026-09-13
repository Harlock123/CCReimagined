using CCReimagined.Core.Codegen;

namespace CCReimagined.Core.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("Unit Cost", "Unit_Cost")]
    [InlineData("member-id", "member_id")]
    [InlineData("2ndAddress", "_2ndAddress")]
    [InlineData("ok_name", "ok_name")]
    public void ToIdentifier_makes_column_names_legal(string raw, string expected) =>
        Assert.Equal(expected, Naming.ToIdentifier(raw));

    [Fact]
    public void ToPropertyName_escapes_an_exact_keyword()
    {
        Assert.Equal("@int", Naming.ToPropertyName("int"));
        Assert.Equal("@class", Naming.ToPropertyName("class"));
    }

    [Fact]
    public void ToPropertyName_leaves_a_name_that_merely_contains_a_keyword_alone()
    {
        // The original tool used a substring test, so INTERVIEWER became Field_INTERVIEWER
        // because it contains "int". A column name should only be escaped on an exact match.
        Assert.Equal("INTERVIEWER", Naming.ToPropertyName("INTERVIEWER"));
        Assert.Equal("DoubleCheck", Naming.ToPropertyName("DoubleCheck"));
    }

    [Fact]
    public void ToPropertyName_renames_a_column_that_would_shadow_a_generated_member() =>
        Assert.Equal("ConnectionStringColumn", Naming.ToPropertyName("ConnectionString"));

    [Theory]
    [InlineData("dbo.tbl_member_main", "TblMemberMain")]
    [InlineData("ORDERS", "Orders")]
    [InlineData("public.line_items", "LineItems")]
    public void ToClassName_pascal_cases_a_relation_name(string relation, string expected) =>
        Assert.Equal(expected, Naming.ToClassName(relation));
}
