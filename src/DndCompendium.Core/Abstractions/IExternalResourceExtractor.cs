using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IExternalResourceExtractor
{
    IReadOnlyList<ExternalResourceReference> Extract(string sourceText);
}
