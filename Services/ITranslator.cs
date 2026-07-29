using Lingo.Models;

namespace Lingo.Services;

internal interface ITranslator
{
    string Name { get; }

    // onPartial 在流式输出时随 token 到达回调已累积的译文；不支持流式的翻译器可忽略该参数
    Task<TranslationResult> TranslateAsync(string text, Action<string>? onPartial, CancellationToken cancellationToken);
}
