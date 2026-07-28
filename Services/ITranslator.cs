using Lingo.Models;

namespace Lingo.Services;

internal interface ITranslator
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken);
}
