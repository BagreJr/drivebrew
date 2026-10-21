using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class AppUpdateReminderPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldNotify_DoesNotRepeatPostponedVersionBeforeReminderInterval()
    {
        var result = AppUpdateReminderPolicy.ShouldNotify(6, 6, Now.AddHours(-2), Now, TimeSpan.FromHours(24));

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotify_RemindsAfterIntervalOrImmediatelyForDifferentVersion()
    {
        Assert.True(AppUpdateReminderPolicy.ShouldNotify(6, 6, Now.AddHours(-24), Now, TimeSpan.FromHours(24)));
        Assert.True(AppUpdateReminderPolicy.ShouldNotify(7, 6, Now.AddMinutes(-1), Now, TimeSpan.FromHours(24)));
    }
}
