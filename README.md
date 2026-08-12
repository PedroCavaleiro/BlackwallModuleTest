# Blackwall Third-Party Modules

Blackwall supports third-party modules that can detect and act on messages across **Discord**, **Twitch**, or **both** platforms simultaneously. Modules are compiled .NET assemblies that are dynamically loaded, evaluated per message, and managed through the Blackwall web UI.

This example project (`EmojiSpamModule`) demonstrates a complete module that works on both Discord and Twitch.

---

## How It Works

1. You write a class library that implements `IBlackwallModule`.
2. You publish the project to a public Git repository with a `blackwall-module.json` manifest at the root.
3. A Blackwall instance owner installs the module via the web UI by providing the Git URL.
4. Blackwall clones the repo, runs `dotnet build`, copies the output DLL, and dynamically loads it.
5. On every incoming message (Discord or Twitch), Blackwall calls `EvaluateAsync` on each enabled module.
6. If a module returns a `ModuleVerdict`, Blackwall performs the requested action (delete, timeout, ban, etc.).

---

## Repository Structure

Your Git repository must follow this layout:

```
my-blackwall-module/
├── blackwall-module.json          # Manifest (required, root of repo)
└── src/                           # Source directory (required)
    ├── MyModule.csproj            # Project file (required, in src/)
    └── MyModule.cs                # Your module code
```

The `src/` directory is mandatory — Blackwall looks for a `.csproj` file inside it and runs `dotnet build -c Release` from that directory.

---

## The Manifest (`blackwall-module.json`)

The manifest is a JSON file placed at the **root** of the repository. It describes the module and its settings schema.

```json
{
  "name": "emoji-spam",
  "readableName": "Emoji Spam",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "Detects messages with excessive emoji.",
  "entryPoint": "MyModule.dll",
  "canPerformActions": true,
  "platforms": ["discord", "twitch"],
  "settingsSchema": {
    "cards": [
      {
        "title": "Detection Threshold",
        "description": "Configure detection sensitivity.",
        "fields": [
          {
            "key": "maxEmojiCount",
            "uiName": "Max Emoji Per Message",
            "helpText": "Messages with more than this many emoji will be flagged.",
            "inputType": "number",
            "defaultValue": "5"
          },
          {
            "key": "countCustomEmoji",
            "uiName": "Count Custom Emoji",
            "helpText": "Whether to count custom server emoji.",
            "inputType": "dropdown",
            "defaultValue": "true",
            "options": [
              { "text": "Yes", "value": "true" },
              { "text": "No", "value": "false" }
            ]
          }
        ]
      }
    ]
  }
}
```

### Manifest Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Unique machine name for the module (lowercase, hyphenated). |
| `readableName` | string | No | Human-friendly name shown in the UI. |
| `version` | string | Yes | Semver version string. |
| `author` | string | Yes | Author name. |
| `description` | string | No | Short description shown in the UI. |
| `entryPoint` | string | Yes | The DLL filename produced by the build (e.g. `MyModule.dll`). Must match the assembly name in your `.csproj`. |
| `canPerformActions` | boolean | Yes | Whether the module can request moderation actions (delete, timeout, ban). If `false`, the actions UI section is hidden. |
| `platforms` | array | Yes | Which platforms this module supports. Valid values: `"discord"`, `"twitch"`. Use both for cross-platform modules. |
| `settingsSchema` | object | No | Defines the settings UI rendered in the Blackwall web panel. |

### Settings Schema

The settings schema is organized into **cards** (visual sections), each containing **fields**.

**Field types** (`inputType`):
- `"number"` — numeric input
- `"text"` — text input
- `"dropdown"` — select dropdown (requires `options` array with `text`/`value` pairs)

Each field has a `key` (used to retrieve the value in code), a `uiName` (label), optional `helpText`, and a `defaultValue`.

---

## Implementing the Module

Your module must implement `IBlackwallModule` from the `Blackwall.Modules.Abstractions` package.

### Project Setup

Create a class library project targeting `net10.0`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>MyModule</AssemblyName>
    <RootNamespace>MyModule</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Blackwall.Modules.Abstractions">
      <HintPath>Blackwall.Modules.Abstractions.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

> **Note:** You need the `Blackwall.Modules.Abstractions.dll` file available locally to compile. You can get it by building the `Blackwall.Modules.Abstractions` project from the main Blackwall repository and copying the DLL into your `src/` directory, or by publishing it as a NuGet package and using a `PackageReference`.

### The `IBlackwallModule` Interface

```csharp
public interface IBlackwallModule {
    string Name { get; }
    string Version { get; }

    Task InitializeAsync(ModuleSettings settings, CancellationToken ct);

    Task<ModuleVerdict?> EvaluateAsync(ModuleMessageContext context, CancellationToken ct);

    Task UpdateSettingsAsync(ModuleSettings settings, CancellationToken ct);
}
```

- **`Name`** / **`Version`** — Should match the manifest.
- **`InitializeAsync`** — Called once when the module is loaded. Read your settings here.
- **`EvaluateAsync`** — Called for every incoming message. Return a `ModuleVerdict` to flag the message, or `null` to allow it.
- **`UpdateSettingsAsync`** — Called when settings are changed at runtime. Typically delegates to `InitializeAsync`.

### Full Example

```csharp
using System.Text.RegularExpressions;
using Blackwall.Modules.Abstractions;

namespace MyModule;

public sealed class MyModule : IBlackwallModule
{
    public string Name => "my-module";
    public string Version => "1.0.0";

    private int _threshold = 5;
    private ModuleAction _action = ModuleAction.DeleteOnly;
    private int _timeoutMinutes = 5;
    private bool _autoLockdown = false;

    public Task InitializeAsync(ModuleSettings settings, CancellationToken ct)
    {
        _threshold = settings.GetInt32("threshold") ?? 5;
        _timeoutMinutes = settings.GetInt32("__timeoutMinutes") ?? 5;
        _autoLockdown = settings.GetBoolean("__autoLockdown") ?? false;

        var actionStr = settings.Get("__action");
        if (!string.IsNullOrEmpty(actionStr) && Enum.TryParse<ModuleAction>(actionStr, true, out var action))
            _action = action;

        return Task.CompletedTask;
    }

    public Task<ModuleVerdict?> EvaluateAsync(ModuleMessageContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.Content))
            return Task.FromResult<ModuleVerdict?>(null);

        // Your detection logic here
        if (context.Content.Length > _threshold * 100)
        {
            return Task.FromResult<ModuleVerdict?>(new ModuleVerdict(
                ViolationType: "long-message",
                Action: _action,
                TimeoutMinutes: _timeoutMinutes,
                DeleteDays: 0,
                AutoLockdown: _autoLockdown,
                Reason: $"Message too long ({context.Content.Length} chars)"
            ));
        }

        return Task.FromResult<ModuleVerdict?>(null);
    }

    public Task UpdateSettingsAsync(ModuleSettings settings, CancellationToken ct)
        => InitializeAsync(settings, ct);
}
```

---

## Platform-Aware Modules

The `ModuleMessageContext` includes a `Platform` field so your module can behave differently on Discord vs Twitch:

```csharp
public Task<ModuleVerdict?> EvaluateAsync(ModuleMessageContext context, CancellationToken ct)
{
    // Only check for Discord custom emoji on Discord
    if (context.Platform == ModulePlatform.Discord)
    {
        var customEmojiCount = Regex.Matches(context.Content, "<a?:\\w+:\\d+>").Count;
        // ...
    }

    // Twitch doesn't have custom emoji, but has Twitch emotes
    if (context.Platform == ModulePlatform.Twitch)
    {
        // Twitch-specific logic
    }

    // Shared logic for both platforms
    // ...

    return Task.FromResult<ModuleVerdict?>(null);
}
```

### `ModuleMessageContext` Fields

| Field | Type | Description |
|---|---|---|
| `Platform` | `ModulePlatform` | `Discord` or `Twitch` |
| `CommunityId` | `long` | Discord guild ID or Twitch channel user ID |
| `UserId` | `long` | The message author's platform user ID |
| `ChannelId` | `long` | Discord channel ID or Twitch channel user ID |
| `ChannelName` | `string` | Channel name (e.g. Discord channel name or Twitch channel login) |
| `Username` | `string` | Author's username |
| `IsBot` | `bool` | Whether the author is the broadcaster (Twitch) or a bot (Discord) |
| `Content` | `string` | The message text |
| `Attachments` | `IReadOnlyList<ModuleAttachment>` | Message attachments (Discord only; empty on Twitch) |
| `Embeds` | `IReadOnlyList<ModuleEmbed>` | Message embeds (Discord only; empty on Twitch) |
| `MessageTimestampUtc` | `DateTime` | When the message was sent |

### Supported Platforms

Choose which platforms to support by setting the `platforms` field in your manifest:

- **Discord only:** `"platforms": ["discord"]`
- **Twitch only:** `"platforms": ["twitch"]`
- **Both platforms:** `"platforms": ["discord", "twitch"]`

Blackwall will refuse to install a module on a platform that isn't listed in the manifest.

---

## Actions and Settings

### Module Actions

When `canPerformActions` is `true` in the manifest, the Blackwall UI shows an actions configuration card. The user can choose:

| Action | Discord | Twitch |
|---|---|---|
| `deleteOnly` | Delete the message | Delete the message |
| `timeout` | Timeout the user | Timeout the user |
| `kick` | Kick the user | N/A (treated as timeout) |
| `ban` | Ban the user | Ban the user |
| `softBan` | Ban + unban (removes recent messages) | N/A (treated as ban) |

These are stored in settings with reserved keys:

| Key | Description |
|---|---|
| `__action` | The chosen `ModuleAction` (string, e.g. `"timeout"`) |
| `__timeoutMinutes` | Timeout duration in minutes |
| `__messageDeleteDays` | Days of message history to purge (Discord bans, max 7) |
| `__autoLockdown` | Whether to trigger a server/channel lockdown (`"true"` / `""false"`) |

Read these in `InitializeAsync` as shown in the examples above.

### Custom Settings

Your own settings (defined in `settingsSchema`) are stored alongside the reserved action keys. Access them with `settings.Get("yourKey")`, `settings.GetInt32("yourKey")`, or `settings.GetBoolean("yourKey")`.

---

## Evaluation Lifecycle

1. Module is loaded and `InitializeAsync` is called with the current settings.
2. For each incoming message, `EvaluateAsync` is called with a 5-second timeout.
3. If the module returns a `ModuleVerdict`:
   - The message is deleted.
   - The configured action (timeout/ban/etc.) is applied to the user.
   - The event is logged to the audit trail (if enabled).
   - Lockdown is triggered (if `AutoLockdown` is set).
4. If the module returns `null`, the message passes through.
5. If `EvaluateAsync` throws or times out, the module is skipped and the error is logged.

> **Important:** `EvaluateAsync` must be fast and non-blocking. If it exceeds 5 seconds, it will be cancelled and the message will pass through. Use `async`/`await` for any I/O and respect the `CancellationToken`.

---

## Installing a Module

1. Push your module to a public Git repository (GitHub, GitLab, etc.).
2. In the Blackwall web UI, navigate to your Discord server or Twitch channel configuration.
3. Go to the **Modules** tab.
4. Enter the HTTPS Git URL (e.g. `https://github.com/user/my-blackwall-module.git`).
5. Click **Install**.

Blackwall will:
- Clone the repository (shallow clone)
- Validate the manifest
- Run `dotnet build -c Release` in the `src/` directory
- Copy the built DLL to its modules directory
- Load the module and begin evaluating messages

### Updating a Module

Click **Update** next to an installed module. Blackwall re-clones, rebuilds, and reloads the module. The module version in the manifest should be incremented for each release.

### Uninstalling

Click **Uninstall** to remove the module. The DLL is unloaded from memory and the installation record is deleted from the database.

---

## This Example: Emoji Spam Module

The `EmojiSpamModule` in this repository demonstrates:

- Cross-platform support (Discord + Twitch)
- Custom settings schema with number and dropdown inputs
- Action configuration (timeout, ban, etc.)
- Platform-aware detection (custom emoji regex only matches Discord format)
- Settings hot-reload via `UpdateSettingsAsync`

### Files

```
Blackwall.EmojiSpamModule/
├── blackwall-module.json
├── README.md
└── src/
    ├── Blackwall.EmojiSpamModule.csproj
    └── EmojiSpamModule.cs
```
