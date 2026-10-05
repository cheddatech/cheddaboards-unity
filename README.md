<p align="center">
  <img src="assets/logo.png" alt="CheddaBoards" width="420">
</p>

# CheddaBoards Unity SDK

**Leaderboards, achievements, and auth for Unity. Any platform. 3-minute setup.**

Drop-in C# SDK for [CheddaBoards](https://cheddaboards.com) — permanent, serverless gaming infrastructure powered by the Internet Computer.

[![Website](https://img.shields.io/badge/website-cheddaboards.com-blue)](https://cheddaboards.com)
[![Docs](https://img.shields.io/badge/docs-docs.cheddaboards.com-blue)](https://docs.cheddaboards.com)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Version](https://img.shields.io/badge/version-2.3.1-green)]()
[![API uptime](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fcheddatech%2Fstatus%2FHEAD%2Fapi%2Fapi%2Fuptime.json&label=API%20uptime)](https://status.cheddatech.com)
[![Leaderboards uptime](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fcheddatech%2Fstatus%2FHEAD%2Fapi%2Fleaderboards-on-chain%2Fuptime.json&label=leaderboards%20uptime)](https://status.cheddatech.com)

---

## What's new

- **2.3.1** — Shared-device fix: switching player with SetPlayerId() now resets the previous player's state and drops their in-flight responses.
- **2.3.0** — Device-code sign-in now survives an app restart or WebGL reload: the pending code is saved and polling resumes on the same code, so a player who comes back to a reloaded game is still signed in once they approve. `LoginWithDeviceCode()` reuses a pending code instead of minting a new one (pass `true` to force a fresh one); new `HasPendingDeviceCode()`, `GetDeviceVerificationUrl()` and `GetDeviceCodeSecondsRemaining()` for login screens. Also fixed: `GetGameStats()` (was hitting a route that doesn't exist), direct board reads no longer retry a 404 via the proxy, `GetAuthType()` reports the real provider after linking (Apple sign-ins were labelled google), linked accounts are created with the player's chosen nickname, and `OnAccountUpgradeFailed` actually fires. See [CHANGELOG.md](CHANGELOG.md).
- **2.2.7** — Three fixes on the anonymous-player paths: submits no longer silently overwrite a player's saved nickname with a generated one; batch achievement sync reports the real synced ids instead of a false "0 synced"; and `GetAchievements()` works again (it now reads from the profile — the standalone route it called never existed). Until a new player's profile loads, `GetNickname()` returns "", so show "Guest"; the server assigns a name (e.g. `Player_1248`) on their first submit.
- **2.2.6** — Board reads now come **straight from the CheddaBoards canister** for faster loads, with automatic proxy fallback (see 2.2.5). Fixed the `GetAlltimeLeaderboard()` / `GetWeeklyLeaderboard()` helpers, which queried the wrong board IDs. `GetLeaderboard()` default limit is now 100.
- **2.2.5** — Direct canister reads: `GetScoreboard()` and the Weekly / Daily / Alltime / Monthly helpers read directly from the Internet Computer, keyless and CORS-simple, falling back to the proxy automatically if the direct path can't get through. Same events, no code changes.
- **2.2.3** — Sessions persist across restarts (device-code sign-in is now a one-time flow), plus a new `OnSessionExpired` event. Nickname validation matches the canonical rule (3–16 chars, letters/numbers/underscores).

Full history: [CHANGELOG.md](CHANGELOG.md) and the header comment of `CheddaBoards.cs`.

---

## Features

- **Leaderboards** — All Time, Weekly, Daily, Monthly, and custom-interval, with automatic archiving
- **Category Scoreboards** — Targeted per-level / per-mode boards; submit to one board by ID
- **Achievements** — Unlock tracking with batch sync support
- **Multi-Auth** — Anonymous, Google, Apple, Internet Identity via Device Code flow
- **Play Sessions** — Server-side time validation for anti-cheat
- **Cross-Game Profiles** — Players keep one identity across all CheddaBoards games
- **Account Migration** — Upgrade anonymous accounts to verified without losing data
- **HTTP-Only** — No JavaScript bridges. Works identically on all platforms
- **Zero Dependencies** — Pure UnityWebRequest. No third-party packages required

---

## Quick Start

### 1. Add to your project

Requires **Unity 2022.3 LTS or newer**. Copy `CheddaBoards.cs` into your Unity project (e.g. `Assets/Scripts/CheddaBoards.cs`). The SDK lives in the `CheddaTech` namespace, so add `using CheddaTech;` at the top of any script that uses it.

### 2. Configure

```csharp
using CheddaTech;

var cb = CheddaBoards.Instance; // Auto-creates singleton GameObject
cb.SetApiKey("your-api-key");
cb.SetGameId("your-game-id");
```

Get your API key and game ID from the [CheddaBoards Dashboard](https://cheddaboards.com).

### 3. Login and submit scores

```csharp
void Start()
{
    var cb = CheddaBoards.Instance;
    cb.SetApiKey("your-api-key");
    cb.SetGameId("your-game-id");

    cb.OnLoginSuccess += (nickname) => Debug.Log($"Welcome {nickname}!");
    cb.OnScoreSubmitted += (score, streak) => Debug.Log($"Score saved: {score}");

    cb.LoginAnonymous(); // no name: returning players keep their saved nickname
}

void OnGameOver(int score, int streak)
{
    CheddaBoards.Instance.SubmitScore(score, streak);
}
```

That's it. Leaderboard data is permanently stored on-chain.

Full walkthrough and REST reference: **[docs.cheddaboards.com](https://docs.cheddaboards.com)**.

---

## Demo — CheddaClick

A complete working example lives in [`Demo/`](Demo/): **CheddaClick**, a
30-second cheese-clicking game in one script. It shows the full integration —
anonymous login, guest flow, play sessions, score submit, leaderboard render,
delta-synced achievements with a standing unlock panel, play count, and
nickname changes.

To run it: open `Demo/CheddaClick.unity`, put your API key and game ID on the
`CheddaClickGame` component (or in `CheddaBoards.cs`), and press Play.
The demo UI uses TextMeshPro; if your project doesn't have it yet, Unity will prompt you to import TMP Essentials when you open the scene (the SDK itself has no dependencies).
`CheddaClickGame.cs` is commented as a reference — the event wiring and the
anonymous-player ordering (profile before play, achievements after submit) are
the patterns to copy into your own game.

---

## Authentication

### Anonymous Login

Instant login with a persistent device ID. No account creation needed.

```csharp
cb.OnLoginSuccess += (nickname) => Debug.Log("Logged in: " + nickname);
cb.OnLoginFailed += (error) => Debug.Log("Failed: " + error);

cb.LoginAnonymous();
```

Log in **without** a name unless the player has just chosen one: any name you
pass becomes the current nickname and is written to the server on the next
submit, overwriting whatever the player had saved. Leave it empty and returning
players keep their stored name; brand-new players get a server-assigned name
(e.g. `Player_1248`) when their first submit creates their profile. Until the
profile loads, `GetNickname()` returns `""`, so show "Guest" locally. Players can
pick their own name any time with `ChangeNickname()`.

> **Nickname rules (server-enforced):** 3–16 characters, letters, numbers, and
> underscores. Anything else is rejected on nickname changes; names supplied at
> login are sanitized server-side. If a name is already taken, the server
> assigns a suffixed variant (e.g. `Player_1`). See
> [Authentication](https://docs.cheddaboards.com/api/authentication).

### Social Login (Google / Apple / Internet Identity)

Uses the Device Code Auth flow (RFC 8628) — works on every platform including consoles, VR, and native builds. No browser pop-ups needed in-game.

```csharp
// code/url let the player authorise at cheddaboards.com/link.
// qrDataUrl is a base64 PNG data URL — display it as a scannable QR, or just show the code.
cb.OnDeviceCodeReceived += (code, url, qrDataUrl) =>
{
    codeLabel.text = $"Go to {url}\nEnter code: {code}";
};

cb.OnDeviceCodeApproved += (nickname) =>
{
    Debug.Log($"Welcome {nickname}!");
    // Player is now authenticated with their social account
};

cb.OnDeviceCodeExpired += () => Debug.Log("Code expired, try again");

cb.LoginWithDeviceCode();
```

A pending code survives an app restart or page reload (it's saved to `PlayerPrefs` and polling resumes on the same code), and `LoginWithDeviceCode()` re-emits that code rather than minting a new one, so a login screen can just call it on startup. Use `HasPendingDeviceCode()` to skip straight to "waiting for approval", and `GetDeviceVerificationUrl()` / `GetDeviceCodeSecondsRemaining()` to redraw a restored code with its real time left. Pass `LoginWithDeviceCode(true)` to force a fresh code.

> **Closing the popup is not cancelling.** If the player dismisses the code/QR popup, just hide it and leave polling running; `OnDeviceCodeApproved` / `OnLoginSuccess` still fire when their phone finishes. Only call `CancelDeviceCode()` on an explicit "Cancel" or "Use a different account", since an approval given after that call is never picked up.

Full device-code flow, including QR rendering: [Device code login](https://docs.cheddaboards.com/concepts/device-code).

### Account Migration

Upgrade an anonymous player to a verified account without losing scores or achievements. Scores and streaks merge by maximum, achievements are deduplicated, and play counts are summed — linking the same account from a second device merges cleanly.

```csharp
cb.OnAccountUpgraded += (profile, migration) =>
{
    // profile: the merged account's profile; migration: migratedGames / migratedScoreboards counts
    Debug.Log($"Account upgraded! {migration["migratedScoreboards"]} boards merged.");
};
cb.OnAccountUpgradeFailed += (error) => Debug.Log($"Migration failed: {error}");

cb.MigrateAnonymousToCurrent(anonymousDeviceId);
```

---

## Scores & Leaderboards

### Submit a Score

```csharp
cb.OnScoreSubmitted += (score, streak) => Debug.Log($"Saved: {score}");
cb.OnScoreError += (error) => Debug.Log($"Error: {error}");

cb.SubmitScore(1500, 5); // score, streak
```

`SubmitScore` fans the score out to every standard board on your game (all-time, weekly, daily…).

### Submit to a Specific Board (Category Scoreboards)

For per-level, per-mode, or per-category leaderboards, submit to one board by ID. Unlike `SubmitScore`, this writes to that board **only** — it does not fan out to your other boards or touch the player's overall profile total.

```csharp
cb.OnScoreSubmittedToBoard += (boardId, score, streak) => Debug.Log($"Saved to {boardId}: {score}");

cb.SubmitScoreToBoard("level-14", 1500, 5); // boardId, score, streak
```

The board must be configured as **targeted** in the dashboard. You can call it several times in a row for different boards (e.g. a per-level board plus a shared `runs` board). Failures come back on `OnScoreError`. More detail: [Category boards](https://docs.cheddaboards.com/concepts/category-boards).

### Submit Score with Achievements

Achievements sync automatically after the score is confirmed:

```csharp
var achievements = new List<string> { "first_win", "high_scorer", "streak_5" };
cb.SubmitScoreWithAchievements(2000, 10, achievements);
```

### Get Scoreboards

```csharp
// Time-based scoreboards
cb.OnScoreboardLoaded += (id, config, entries) =>
{
    foreach (Dictionary<string, object> entry in entries)
    {
        Debug.Log($"#{entry["rank"]} {entry["nickname"]}: {entry["score"]}");
    }
};

cb.GetWeeklyLeaderboard();
cb.GetDailyLeaderboard();
cb.GetAlltimeLeaderboard();
cb.GetMonthlyLeaderboard();

// Or by scoreboard ID — works for any board, timed or targeted
cb.GetScoreboard("weekly", 100);
cb.GetScoreboard("level-14", 100);
```

Board reads are served directly from the canister for speed, with automatic proxy fallback — see [Scoreboards](https://docs.cheddaboards.com/api/scoreboards).

### Get Player Rank

```csharp
cb.OnScoreboardRankLoaded += (scoreboardId, rank, score, streak, total) =>
{
    Debug.Log($"You are #{rank} out of {total} players");
};

cb.GetScoreboardRank("weekly");
```

### Browse Archives

View previous periods (last week's results, last month, etc.):

```csharp
cb.OnArchivedScoreboardLoaded += (archiveId, config, entries) =>
{
    Debug.Log($"Archive from {config["periodStart"]} to {config["periodEnd"]}");
};

cb.GetLastWeekScoreboard();
cb.GetLastMonthScoreboard();
cb.GetYesterdayScoreboard();
```

Reset schedules and archive retention: [Timed leaderboards](https://docs.cheddaboards.com/concepts/timed-leaderboards).

---

## Achievements

```csharp
cb.OnAchievementUnlocked += (id) => Debug.Log($"Unlocked: {id}");

// Single
cb.UnlockAchievement("first_win");

// Batch
cb.UnlockAchievementsBatch(new List<string> { "first_win", "speed_run" });

// Load player's achievements (reads from the player's profile)
cb.OnAchievementsLoaded += (achievements) => Debug.Log($"Got {achievements.Count} achievements");
cb.GetAchievements();
```

---

## Play Sessions (Anti-Cheat)

Server-side time validation ensures scores match actual play time:

```csharp
cb.OnPlaySessionStarted += (token) => Debug.Log("Session started");

// Start when gameplay begins
cb.StartPlaySession();

// Submit score — play session token is attached automatically
// (applies to both SubmitScore and SubmitScoreToBoard)
cb.SubmitScore(score, streak);

// End when player quits or pauses
cb.EndPlaySession();
```

Caps, time validation, and the suspicion log: [Anti-cheat](https://docs.cheddaboards.com/concepts/anti-cheat).

---

## Events Reference

| Event | Parameters | Description |
|-------|-----------|-------------|
| `OnSdkReady` | — | SDK initialised |
| `OnLoginSuccess` | `nickname` | Login completed |
| `OnLoginFailed` | `error` | Login failed |
| `OnLogoutSuccess` | — | Logged out |
| `OnSessionExpired` | — | Stored session rejected by server (401/403); `OnLogoutSuccess` also fires |
| `OnScoreSubmitted` | `score, streak` | Score saved (fan-out) |
| `OnScoreSubmittedToBoard` | `boardId, score, streak` | Targeted score saved to one board |
| `OnScoreError` | `error` | Score submission failed |
| `OnScoreboardError` | `error` | Scoreboard read failed |
| `OnPlaySessionError` | `error` | Play session couldn't start (score still submits unless time validation is on) |
| `OnNicknameError` | `error` | Nickname rejected (invalid — don't retry the same value) |
| `OnDeviceCodeError` | `error` | Device code sign-in failed |
| `OnScoreboardLoaded` | `id, config, entries` | Scoreboard data received |
| `OnScoreboardRankLoaded` | `id, rank, score, streak, total` | Player rank received |
| `OnAchievementUnlocked` | `achievementId` | Achievement unlocked |
| `OnAchievementsLoaded` | `achievements` | Achievement list received |
| `OnPlaySessionStarted` | `token` | Play session active |
| `OnDeviceCodeReceived` | `code, url, qrDataUrl` | Device code ready to display |
| `OnDeviceCodeApproved` | `nickname` | Social login completed |
| `OnDeviceCodeExpired` | — | Code timed out |
| `OnAccountUpgraded` | `profile, migration` | Migration completed (`migration` carries `migratedGames` / `migratedScoreboards`) |
| `OnAccountUpgradeFailed` | `error` | Anonymous-to-account migration failed (the login itself still succeeded) |
| `OnProfileLoaded` | `nickname, score, streak, achievements, playCount` | Profile data received |
| `OnNicknameChanged` | `nickname` | Nickname updated |
| `OnArchivesListLoaded` | `scoreboardId, archives` | Archive list received |
| `OnArchivedScoreboardLoaded` | `archiveId, config, entries` | Archived scoreboard data |

---

## Utility Methods

```csharp
cb.IsAuthenticated()     // true if logged in
cb.HasAccount()          // true if logged in with a non-anonymous account
cb.IsAnonymous()         // true if using anonymous auth
cb.CanConnect()          // true if API key or session is set
cb.GetNickname()         // current nickname ("" for unnamed anonymous — show "Guest")
cb.GetHighScore()        // cached high score
cb.GetBestStreak()       // cached best streak
cb.GetPlayCount()        // cached play count
cb.GetPlayerId()         // persistent device ID
cb.GetAuthType()         // "anonymous", "google", "apple", ...

cb.HasPendingDeviceCode()            // an unexpired device code is waiting for approval
cb.GetDeviceUserCode()               // the code to show the player
cb.GetDeviceVerificationUrl()        // the link page URL for that code
cb.GetDeviceCodeSecondsRemaining()   // real time left (less than 300 after a reload)
```

---

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `apiKey` | — | Your CheddaBoards API key |
| `gameId` | — | Your game ID |
| `debugLogging` | `false` | Enable verbose console logging (set `true` while developing) |

The SDK auto-creates a singleton `GameObject` with `DontDestroyOnLoad`. No manual scene setup required.

---

## Platform Support

The SDK is HTTP-only — it works identically everywhere Unity runs:

- Windows, Mac, Linux
- iOS, Android
- WebGL
- Consoles
- VR/AR

---

## Links

- **Docs**: [docs.cheddaboards.com](https://docs.cheddaboards.com) — guides and full REST API reference
- **Website**: [cheddaboards.com](https://cheddaboards.com)
- **Service status**: [status.cheddatech.com](https://status.cheddatech.com) — check here first if scores stop submitting
- **Godot SDK**: [CheddaBoards-Godot](https://github.com/cheddatech/CheddaBoards-Godot)
- **Backend (open source)**: [cheddaboards](https://github.com/cheddatech/cheddaboards) — the canister this all runs on
- **Company**: [cheddatech.com](https://cheddatech.com)
- **X**: [@cheddatech](https://x.com/cheddatech)

---

## License

MIT — see [LICENSE](LICENSE)