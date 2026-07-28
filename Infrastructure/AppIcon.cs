namespace Lingo.Infrastructure;

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon is null)
        {
            using Stream? stream = typeof(AppIcon).Assembly
                .GetManifestResourceStream("Lingo.Assets.Lingo.ico");
            _icon = stream is null ? SystemIcons.Application : new Icon(stream);
        }

        return _icon;
    }
}
