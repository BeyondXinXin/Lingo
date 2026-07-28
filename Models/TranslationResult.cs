namespace Lingo.Models;

internal sealed class TranslationResult
{
    public required bool Success { get; init; }
    public required string TranslatorName { get; init; }
    public string Text { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public TimeSpan Elapsed { get; init; }

    public static TranslationResult Ok(string translatorName, string text, TimeSpan elapsed) => new()
    {
        Success = true,
        TranslatorName = translatorName,
        Text = text,
        Elapsed = elapsed,
    };

    public static TranslationResult Failure(string translatorName, string errorMessage, TimeSpan elapsed) => new()
    {
        Success = false,
        TranslatorName = translatorName,
        ErrorMessage = errorMessage,
        Elapsed = elapsed,
    };
}
