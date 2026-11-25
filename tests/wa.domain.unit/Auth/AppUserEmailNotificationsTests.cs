using wa.domain;

namespace wa.domain.unit.Auth;

/// <summary>F-XCT-003 (FR-046-1/2): the single NULL→true rule — null = on, false = off, true = explicitly on.</summary>
public class AppUserEmailNotificationsTests
{
    [Theory]
    [InlineData(null, true)] // never touched → on
    [InlineData(false, false)] // turned off
    [InlineData(true, true)] // explicitly back on
    public void EmailNotificationsOn_ThreeStates_MapToEffective(bool? stored, bool expected) =>
        Assert.Equal(expected, AppUser.EmailNotificationsOn(stored));
}
