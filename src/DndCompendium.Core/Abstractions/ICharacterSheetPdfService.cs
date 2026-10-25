using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface ICharacterSheetPdfService
{
    Task WriteAsync(
        Stream template,
        Stream output,
        CharacterSheetData data,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetData> ReadAsync(
        Stream input,
        CancellationToken cancellationToken = default);
}
