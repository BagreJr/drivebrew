namespace DndCompendium.Core.Models;

/// <summary>
/// Describes whether every required remote resource needed by a local document is usable offline.
/// The TXT itself can remain installed even when this state is <see cref="Incomplete"/>.
/// </summary>
public enum DocumentOfflineState
{
    Complete,
    Pending,
    Incomplete
}
