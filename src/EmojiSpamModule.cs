using System.Text.RegularExpressions;

using Blackwall.Modules.Abstractions;

namespace Blackwall.EmojiSpamModule;

public sealed partial class EmojiSpamModule : IBlackwallModule
{
    private static readonly Regex UnicodeEmojiRegex = CreateUnicodeEmojiRegex();
    private static readonly Regex CustomEmojiRegex = CreateCustomEmojiRegex();

    private int _maxEmojiCount = 5;
    private bool _countCustomEmoji = true;
    private ModuleAction _action = ModuleAction.DeleteOnly;
    private int _timeoutMinutes = 5;
    private int _messageDeleteDays = 0;
    private bool _autoLockdown = false;

    public string Name => "emoji-spam";
    public string Version => "1.0.0";

    public Task InitializeAsync(ModuleSettings settings, CancellationToken ct)
    {
        _maxEmojiCount = settings.GetInt32("maxEmojiCount") ?? 5;
        _countCustomEmoji = settings.GetBoolean("countCustomEmoji") ?? true;
        _timeoutMinutes = settings.GetInt32("__timeoutMinutes") ?? 5;
        _messageDeleteDays = settings.GetInt32("__messageDeleteDays") ?? 0;
        _autoLockdown = settings.GetBoolean("__autoLockdown") ?? false;

        var actionStr = settings.Get("__action");
        if (!string.IsNullOrEmpty(actionStr) && Enum.TryParse<ModuleAction>(actionStr, true, out var action))
            _action = action;

        return Task.CompletedTask;
    }

    public Task<ModuleVerdict?> EvaluateAsync(ModuleMessageContext context, CancellationToken ct)
    {
        if (context.Content is null)
            return Task.FromResult<ModuleVerdict?>(null);

        var count = UnicodeEmojiRegex.Matches(context.Content).Count;

        if (_countCustomEmoji)
            count += CustomEmojiRegex.Matches(context.Content).Count;

        if (count > _maxEmojiCount)
        {
            return Task.FromResult<ModuleVerdict?>(new ModuleVerdict(
                ViolationType: "emoji-spam",
                Action: _action,
                TimeoutMinutes: _timeoutMinutes,
                DeleteDays: _messageDeleteDays,
                AutoLockdown: _autoLockdown,
                Reason: $"Message contains {count} emoji (limit: {_maxEmojiCount})"
            ));
        }

        return Task.FromResult<ModuleVerdict?>(null);
    }

    public Task UpdateSettingsAsync(ModuleSettings settings, CancellationToken ct)
        => InitializeAsync(settings, ct);

    [GeneratedRegex(@"(?:\uD83C[\uDC00-\uDFFF]|\uD83D[\uDC00-\uDFFF]|\uD83E[\uDC00-\uDEFF])|[\u2600-\u27BF\uFE0F]", RegexOptions.CultureInvariant)]
    private static partial Regex CreateUnicodeEmojiRegex();

    [GeneratedRegex(@"<a?:\w+:\d+>", RegexOptions.CultureInvariant)]
    private static partial Regex CreateCustomEmojiRegex();
}
