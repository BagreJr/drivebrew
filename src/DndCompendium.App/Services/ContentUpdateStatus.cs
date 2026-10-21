using DndCompendium.Core.Models;

namespace DndCompendium.App.Services;

public enum ContentUpdateState
{
    Checking,
    Current,
    Problem
}

public sealed class ContentUpdateStatus
{
    public event EventHandler? Changed;

    public ContentUpdateState State { get; private set; } = ContentUpdateState.Checking;
    public string Label { get; private set; } = "Comprobando…";

    public void SetChecking()
    {
        State = ContentUpdateState.Checking;
        Label = "Comprobando…";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetResult(DocumentUpdateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.FailedFileNames.Count > 0)
        {
            State = ContentUpdateState.Problem;
            Label = result.FailedFileNames.Count == 1
                ? "✕ 1 pendiente"
                : $"✕ {result.FailedFileNames.Count} pendientes";
        }
        else
        {
            State = ContentUpdateState.Current;
            Label = result.UpdatedFileNames.Count > 0 ? "✓ Actualizado" : "✓ Al día";
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetUnavailable()
    {
        State = ContentUpdateState.Problem;
        Label = "✕ Sin verificar";
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
