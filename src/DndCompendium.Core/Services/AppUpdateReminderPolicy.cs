namespace DndCompendium.Core.Services;

public static class AppUpdateReminderPolicy
{
    public static bool ShouldNotify(
        int availableVersionCode,
        int postponedVersionCode,
        DateTimeOffset? postponedAt,
        DateTimeOffset now,
        TimeSpan reminderInterval)
    {
        if (availableVersionCode <= 0)
            return false;

        return postponedVersionCode != availableVersionCode
            || postponedAt is null
            || now - postponedAt.Value >= reminderInterval;
    }
}
