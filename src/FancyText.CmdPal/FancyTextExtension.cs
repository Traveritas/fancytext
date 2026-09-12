using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace FancyText.CmdPal;

/// <summary>
/// CmdPal 扩展实例。ClassId 必须与 Package.appxmanifest 中
/// com:Class/@Id 与 CreateInstance/@ClassId 一致（1412aa48-2c1f-47f5-8c0a-281b9e2f65e0）。
/// </summary>
[Guid("1412aa48-2c1f-47f5-8c0a-281b9e2f65e0")]
public sealed partial class FancyTextExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly FancyTextCommandsProvider _provider = new();

    public FancyTextExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose() => _extensionDisposedEvent.Set();
}
