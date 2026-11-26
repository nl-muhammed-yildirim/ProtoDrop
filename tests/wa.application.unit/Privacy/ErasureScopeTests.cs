using System.Reflection;
using wa.application.unit.Privacy.Fixtures;
using wa.domain;
using wa.domain.Privacy;

namespace wa.application.unit.Privacy;

/// <summary>F-XCT-005 US-048-03 (AC-048-3): the closed erasure list and the [Pii] marks stay in sync, and each disposition matches the model.</summary>
public class ErasureScopeTests
{
    private static IEnumerable<PropertyInfo> PiiProperties() =>
        typeof(AppUser).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Concat(PiiConvention.ModelStringColumns())
            .DistinctBy(p => (p.DeclaringType, p.Name));

    [Fact]
    public void MarkedColumns_EqualTheClosedList() =>
        Assert.Empty(PiiConvention.ClosedListViolations(PiiProperties(), [.. ErasureScope.AllColumns]));

    [Fact]
    public void MarkedFixture_NotInClosedList_Fails()
    {
        var props = typeof(MarkedEmailFixture).GetProperties();
        var v = Assert.Single(PiiConvention.ClosedListViolations(props, [.. ErasureScope.AllColumns]));
        Assert.Contains("MarkedEmailFixture.Email", v);
    }

    [Fact]
    public void MarkedFixture_InClosedList_Passes()
    {
        var props = typeof(MarkedEmailFixture).GetProperties();
        ErasureColumn[] list = [.. ErasureScope.AllColumns, new(typeof(MarkedEmailFixture), "Email", ErasureDisposition.Retained, "fixture")];
        Assert.Empty(PiiConvention.ClosedListViolations(props, list));
    }

    [Fact]
    public void EntryWithoutPiiMark_Fails()
    {
        ErasureColumn[] list = [.. ErasureScope.AllColumns, new(typeof(UnmarkedEmailFixture), "Email", ErasureDisposition.Retained, "fixture")];
        var v = Assert.Single(PiiConvention.ClosedListViolations([], list));
        Assert.Contains("UnmarkedEmailFixture.Email", v);
    }

    [Fact]
    public void EveryEntry_IsAMappedStringColumn_Once()
    {
        var model = PiiConvention.Model();
        foreach (var c in ErasureScope.AllColumns)
        {
            var entity = model.FindEntityType(c.Entity);
            Assert.NotNull(entity);
            var prop = entity.FindProperty(c.Property);
            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop.ClrType);
        }
        Assert.Equal(ErasureScope.AllColumns.Count, ErasureScope.AllColumns.Select(c => (c.Entity, c.Property)).Distinct().Count());
    }

    [Fact]
    public void Dispositions_MatchTheModel()
    {
        var model = PiiConvention.Model();
        foreach (var c in ErasureScope.AllColumns)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Rationale), $"{c.Entity.Name}.{c.Property}: blank rationale");
            if (c.Disposition == ErasureDisposition.AccountSoftDelete) Assert.Equal(typeof(AppUser), c.Entity);
            if (c.Disposition == ErasureDisposition.TransferHardDelete)
            {
                var entity = model.FindEntityType(c.Entity)!;
                Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Transfer));
            }
        }
    }

    [Fact]
    public void ClosedList_HasFifteenColumns() => Assert.Equal(15, ErasureScope.AllColumns.Count);
}
