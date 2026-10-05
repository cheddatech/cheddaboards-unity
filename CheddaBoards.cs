// CheddaBoards.cs v2.3.0
// CheddaBoards integration for Unity
// https://github.com/cheddatech/CheddaBoards-Unity
// https://cheddaboards.com
//
// HTTP-ONLY SDK: All platforms use the REST API
// - Anonymous login: API key + persistent device ID
// - Social login (Google, Apple, II): Device Code Auth flow
//   Player authenticates on their phone at cheddaboards.com/link
// - Score submissions, play sessions, achievements: all via HTTP API
//
// v2.3.0 (minor bump: adds public API and changes two behaviours, see below):
//   - Device code linking survives an app restart / page reload. On phones
//     and WebGL home-screen apps, tapping the link URL can reload the game
//     when the player comes back, which wiped the pending code from memory.
//     The game's login screen then called LoginWithDeviceCode() again,
//     minted a fresh code, and the player looped forever: each approval
//     landed on a code the SDK had already forgotten. The pending code
//     (device_code, user_code, link URL, QR, expiry) is now saved to
//     PlayerPrefs when it arrives. On startup, if an unexpired code is
//     saved and there is no saved session, polling resumes on the SAME
//     code, and a subsequent LoginWithDeviceCode() call re-emits
//     OnDeviceCodeReceived with the saved code instead of requesting a
//     new one, so the UI shows the code the player already approved.
//     The saved code is cleared on approval, expiry, invalid-code,
//     CancelDeviceCode() and Logout(). Pass LoginWithDeviceCode(true) to
//     force a brand-new code. The restore runs in Awake, which can be
//     before a runtime SetGameId(); the code is held and polling starts
//     (or the code is discarded if it was for another game) when
//     SetGameId() is called.
//   - New: HasPendingDeviceCode() reports whether an unexpired code is
//     waiting (restored or in progress) so login screens can skip straight
//     to "waiting for approval" after a reload; GetDeviceVerificationUrl()
//     and GetDeviceCodeSecondsRemaining() let a popup re-show a restored
//     code with its real remaining time.
//   - Fixed: a direct canister read that returned a 4xx (e.g. 404 for a
//     board that doesn't exist) was retried via the proxy as if it were a
//     network failure, costing an extra proxy request for the same 404.
//     Only transport failures and 5xx fall back now, as documented in 2.2.5.
//   - Fixed: GetGameStats() requested /game/stats, which doesn't exist
//     (OnRequestFailed on every call). Now fetches /game like GetGameInfo().
//   - Docs: CancelDeviceCode() is for an explicit "cancel", not for
//     closing the code popup. Closing the popup should just hide it and
//     leave polling running, otherwise a player who dismisses the QR
//     before their phone finishes ends up approved on the link page but
//     still logged out in the game.
//   - Ported from Godot 2.2.4 (never landed in the Unity SDK):
//     * Linked accounts report their real provider (data.provider from
//       the proxy) instead of always "google", so Apple sign-ins are no
//       longer mislabelled. Older proxies that omit it still get "google".
//     * Device code requests seed the player's current in-game nickname,
//       so accounts CREATED via linking are born with the name the player
//       chose instead of "Player_N". Existing accounts are untouched (the
//       canister ignores the nickname for known users).
//     * Fresh-account nickname preservation: when linking creates a new
//       account (isNewUser) for a previously anonymous player with a
//       chosen name, that name is restored after migration instead of
//       being overwritten by the server-generated one. Merges into
//       existing accounts keep the account's own nickname.
//     * OnAccountUpgradeFailed now actually fires on migration failures
//       (it was declared but never invoked).
// v2.2.7:
//   - Submits no longer rename the player. All three submit paths
//     (SubmitScore, SubmitScoreWithAchievements, SubmitScoreToBoard)
//     used to ALWAYS send a nickname, generating a "Player_XXXXXX"
//     fallback when _nickname was empty - so every submit by a returning
//     anonymous player whose profile hadn't loaded yet silently overwrote
//     their saved name. The nickname field is now omitted from the submit
//     body unless the caller actually set one; the server keeps the
//     existing profile name. The profile parser also no longer backfills
//     a generated name into _nickname when a profile arrives unnamed
//     (that read-path leak would have written a generated name back on
//     the next submit). Unnamed anonymous players stay unnamed - render
//     them as "Guest" in your UI (GetNickname() already returns "" for
//     this case).
//   - Batch achievement sync no longer reports "0 synced" on success.
//     The parser read a "synced" count key the server doesn't send (the
//     real key is "unlocked"), and only accepted one exact results shape.
//     Now: (a) the reported count is the number of ids actually parsed;
//     (b) tolerant parsing accepts plain id arrays, alternate array keys
//     (unlocked/syncedIds/achievements), alternate id keys
//     (id/achievement), and non-bool success flags; (c) on HTTP 200, if
//     the body shape still isn't recognised, the REQUESTED ids are
//     reported as synced (raw body logged for diagnosis) - a 200 means
//     the server stored them. Verified against the live API (v1.8.0):
//     data.results[] of {achievementId, success, message}, where
//     re-sends of already-unlocked ids also return success:true.
//     OnAchievementsLoaded now always carries the real synced set.
//   - GetAchievements() works again. It called
//     GET /players/{id}/achievements, a route the API doesn't have
//     ("Unknown endpoint"). Achievements are only exposed on the profile,
//     so it now fetches the profile and surfaces gameProfile.achievements
//     via OnAchievementsLoaded. OnProfileLoaded does not fire for this
//     call; reading achievements from OnProfileLoaded also still works.
// v2.2.6: * Fix: GetAlltimeLeaderboard() queried "all-time-new" and
//            GetWeeklyLeaderboard() queried "weekly-scoreboard" - both wrong
//            board IDs that returned nothing. Now "all-time" and "weekly",
//            matching the auto-created boards every game gets.
//          * GetLeaderboard() default limit is now 100 (was 1000), matching
//            GetScoreboard() and every other getter. Pass a limit explicitly
//            for deeper results; use GetScoreboardRank() to find a position.
// v2.2.5: Direct canister reads. GetScoreboard() (and the Weekly / Daily /
//          Alltime / Monthly helpers built on it) now reads straight from the
//          CheddaBoards canister over the IC HTTP gateway
//          (fdvph-sqaaa-aaaap-qqc4a-cai.raw.icp0.io) instead of the proxy -
//          faster board loads, no cold-start lag, and keyless/header-free so
//          web exports stay CORS-simple. Same JSON, same events, no code
//          changes needed in your game.
//          * Automatic proxy fallback: if a direct read can't get through
//            (network filters raw.icp0.io, a gateway hiccup, a non-JSON error
//            page), the SDK silently retries the identical request via the
//            proxy. After 3 consecutive direct failures it stops trying direct
//            for the session; one success resets the count. A genuine
//            "not found" from the canister is the real answer, not retried.
//          * Writes, ranks, archives, and all authenticated calls stay on the
//            proxy unchanged.
// v2.2.3: Session persistence + the v2.2.4 nickname validation:
//          * The session token from device code auth is saved to PlayerPrefs
//            and restored on startup, so logged-in players stay logged in
//            across app restarts instead of repeating device code auth.
//          * New OnSessionExpired event: fired when the server rejects the
//            stored token (401/403). The saved session is cleared and
//            OnLogoutSuccess also fires so existing menus fall back to
//            their login screen with no changes.
//          * Logout() now clears the saved session.
//          * ChangeNickname() enforces the canonical nickname rule
//            client-side (3-16 chars, letters/numbers/underscores),
//            matching proxy and canister validation.
// v2.2.1: Added SubmitScoreToBoard(scoreboardId, score, streak) +
//          OnScoreSubmittedToBoard event for targeted "category" scoreboards
//          (per-level / per-mode boards). Writes to one board only; does not
//          fan out or touch the player's profile total. The board must be
//          configured as targeted in the dashboard.
// v2.2.0: Brings the Unity SDK to parity with the v2.2.0 Godot release:
//          * BREAKING: OnProfileLoaded now passes playCount as a 5th argument.
//            4-arg handlers must add a trailing int playCount parameter.
//          * BREAKING: OnDeviceCodeReceived now passes qrDataUrl as a 3rd
//            argument (base64 PNG data URL, or "" if the API returns none).
//            2-arg handlers must add a trailing string parameter.
//          * debugLogging now defaults to false. Set CheddaBoards.Instance
//            .debugLogging = true while developing.
//          * Device codes and emails are redacted in log output.
//          * Device-code polling fires an immediate poll on app focus/resume,
//            and uses real-time waits so it keeps polling while the game is
//            paused (Time.timeScale = 0).
//          * Anonymous players with no nickname keep an empty nickname, so UIs
//            can show "Guest" instead of an auto-generated placeholder.
//            GetNickname() now also filters Player_dev_* / Player_p_* and
//            returns "" when unnamed (was: a generated default).
//          * RefreshProfile() always allows the first call; the cooldown
//            applies from the second call onward.
//          * 404 on scoreboard lookups is treated as non-fatal.
//          * ChangeNickname() can be called with no argument.
// v2.1.0: device_code qr_data_url support (see OnDeviceCodeReceived above).
// v2.0.0: HTTP-only SDK. Removed JavaScript bridge / web SDK dependency.
//          All platforms use the same REST API paths.
//          Social login via Device Code Auth (works everywhere).
// v1.9.0: Device Code Auth - cross-platform social login via REST API.
// v1.8.2: Achievement sync is now non-blocking (async/fire-and-forget).
// v1.8.1: Fixed achievements not syncing - score submitted FIRST (creates player),
//          then achievements sent one-at-a-time.
// v1.7.0: Fixed post-upgrade scores creating ghost anonymous accounts
// v1.6.0: Fixed end_play_session body field (token → playSessionToken)
// v1.5.9: Persistent device IDs - anonymous players keep same identity across sessions
//
// Add to scene as singleton GameObject (DontDestroyOnLoad)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CheddaTech
{
    /// <summary>
    /// CheddaBoards SDK for Unity — Leaderboards, Achievements, and Player Profiles.
    /// HTTP-only: works on all platforms without JavaScript bridges.
    /// </summary>
    public class CheddaBoards : MonoBehaviour
    {
        /// <summary>SDK version. Keep in sync with the header changelog.</summary>
        public const string VERSION = "2.3.0";

        // ============================================================
        // SINGLETON
        // ============================================================

        private static CheddaBoards _instance;
        public static CheddaBoards Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("CheddaBoards");
                    _instance = go.AddComponent<CheddaBoards>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // ============================================================
        // QUICK START
        // ============================================================
        // 1. Add CheddaBoards.Instance to your scene (auto-creates singleton)
        // 2. Set your API key: CheddaBoards.Instance.SetApiKey("cb_xxx");
        // 3. Set your game ID: CheddaBoards.Instance.SetGameId("your-game");
        //
        // Anonymous login:
        //    void Start() {
        //        var cb = CheddaBoards.Instance;
        //        cb.OnLoginSuccess += (nick) => Debug.Log("Logged in: " + nick);
        //        cb.LoginAnonymous("PlayerName");
        //    }
        //
        // Social login (Google/Apple via device code):
        //    void Start() {
        //        var cb = CheddaBoards.Instance;
        //        cb.OnDeviceCodeReceived += (code, url, qrDataUrl) =>
        //            codeLabel.text = $"Go to {url}\nEnter code: {code}";
        //        cb.OnDeviceCodeApproved += (nick) => Debug.Log("Welcome " + nick);
        //        cb.LoginWithDeviceCode(); // reuses a pending code after a reload
        //    }
        //
        // Score submission:
        //    void OnGameOver(int score, int streak) {
        //        CheddaBoards.Instance.SubmitScore(score, streak);
        //    }

        // ============================================================
        // EVENTS (C# equivalents of Godot signals)
        // ============================================================

        // --- Initialization ---
        public event Action OnSdkReady;
        #pragma warning disable 0067  // declared for the public API; raised in a future release
        public event Action<string> OnInitError;

        // --- Authentication ---
        public event Action<string> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action OnLogoutSuccess;
        public event Action OnSessionExpired;
        public event Action<string> OnAuthError;

        // --- Profile ---
        public event Action<string, int, int, List<object>, int> OnProfileLoaded;
        public event Action OnNoProfile;
        public event Action<string> OnNicknameChanged;
        public event Action<string> OnNicknameError;

        // --- Scores & Leaderboards (Legacy) ---
        public event Action<int, int> OnScoreSubmitted;
        public event Action<string, int, int> OnScoreSubmittedToBoard;
        public event Action<string> OnScoreError;
        public event Action<List<object>> OnLeaderboardLoaded;
        public event Action<int, int, int, int> OnPlayerRankLoaded;
        public event Action<string> OnRankError;

        // --- Scoreboards (Time-based) ---
        public event Action<List<object>> OnScoreboardsLoaded;
        public event Action<string, Dictionary<string, object>, List<object>> OnScoreboardLoaded;
        public event Action<string, int, int, int, int> OnScoreboardRankLoaded;
        public event Action<string> OnScoreboardError;

        // --- Scoreboard Archives ---
        public event Action<string, List<object>> OnArchivesListLoaded;
        public event Action<string, Dictionary<string, object>, List<object>> OnArchivedScoreboardLoaded;
        public event Action<int, List<object>> OnArchiveStatsLoaded;
        public event Action<string> OnArchiveError;

        // --- Achievements ---
        public event Action<string> OnAchievementUnlocked;
        public event Action<List<object>> OnAchievementsLoaded;

        // --- HTTP API ---
        public event Action<string, string> OnRequestFailed;

        // --- Play Sessions (Time Validation) ---
        public event Action<string> OnPlaySessionStarted;
        public event Action<string> OnPlaySessionError;

        // --- Account Upgrade (Anonymous → Verified) ---
        public event Action<Dictionary<string, object>, Dictionary<string, object>> OnAccountUpgraded;
        public event Action<string> OnAccountUpgradeFailed;
        #pragma warning restore 0067

        // --- Device Code Auth (Cross-platform login) ---
        public event Action<string, string, string> OnDeviceCodeReceived;
        public event Action<string> OnDeviceCodeApproved;
        public event Action OnDeviceCodeExpired;
        public event Action<string> OnDeviceCodeError;

        // ============================================================
        // CONFIGURATION
        // ============================================================

        /// <summary>Set to true to enable verbose logging.</summary>
        public bool debugLogging = false;

        /// <summary>HTTP API base URL.</summary>
        public const string API_BASE_URL = "https://api.cheddaboards.com";

        // Public board reads go straight to the CheddaBoards canister over the
        // IC HTTP gateway (keyless, CORS-simple), with automatic proxy fallback.
        private const string DIRECT_READ_URL = "https://fdvph-sqaaa-aaaap-qqc4a-cai.raw.icp0.io";
        private const int DIRECT_READ_MAX_FAILURES = 3;
        private static readonly HashSet<string> DIRECT_READ_TYPES = new HashSet<string> { "get_scoreboard" };
        private int _directReadFailures = 0;

        /// <summary>Your API key (set via SetApiKey()).</summary>
        public string apiKey = "";

        /// <summary>Your game ID (set via SetGameId()).</summary>
        public string gameId = "cheddaclick-v2";

        private string _playerId = "";
        private string _sessionToken = "";       // For OAuth session-based auth
        private string _playSessionToken = "";    // For time validation

        // ============================================================
        // INTERNAL STATE
        // ============================================================

        private bool _initComplete = false;
        private string _authType = "";
        private Dictionary<string, object> _cachedProfile = new Dictionary<string, object>();
        private bool _nicknameJustChanged = false;
        private string _nickname = "";

        // Rename correctness state (v2.2.8):
        // _playerExistsOnBackend - true once ANY server op confirms the player
        //   (submit success or profile load). ChangeNickname gates on this, NOT
        //   on _cachedProfile - the cache can stay empty long after the first
        //   submit (fetch chained/failed), which used to send real players'
        //   renames down the local-only branch: OnNicknameChanged fired,
        //   nothing was sent, the board never updated. (Same bug as the Godot
        //   SDK rename race, fixed there in 2.2.6.)
        // _pendingServerNickname - a name set locally before the player existed;
        //   re-synced to the server once a profile loads (capped retries).
        // _requestedNickname - the name sent in the last rename PUT, used as a
        //   fallback when a 2xx/ok response doesn't echo the nickname back
        //   (the old handler silently did NOTHING in that case).
        private bool _playerExistsOnBackend = false;
        private string _pendingServerNickname = "";
        private int _pendingRenameAttempts = 0;
        private string _requestedNickname = "";

        // ============================================================
        // PERFORMANCE OPTIMIZATION
        // ============================================================

        private bool _isRefreshingProfile = false;
        private bool _isSubmittingScore = false;
        private float _lastProfileRefresh = 0f;
        private const float PROFILE_REFRESH_COOLDOWN = 2.0f;

        // ============================================================
        // PENDING SCORE SUBMISSION VALUES
        // ============================================================

        private int _pendingScore = 0;
        private int _pendingStreak = 0;

        // ============================================================
        // DEVICE CODE AUTH STATE
        // ============================================================

        private string _deviceCode = "";
        private string _deviceUserCode = "";
        private Coroutine _deviceCodePollCoroutine = null;
        private float _deviceCodePollInterval = 5.0f;
        private double _deviceCodeExpiresAt = 0;
        private bool _isPollingDeviceCode = false;
        private bool _deviceCodePollInFlight = false;
        private bool _deviceCodeApprovedFlag = false;
        // Link URL + QR of the current code, kept so a restored code can be
        // re-emitted to the UI after a reload without a new /auth/device/code call.
        private string _deviceVerificationUrl = "";
        private string _deviceQrDataUrl = "";
        // Game the restored pending code was minted for. Awake runs before a
        // runtime SetGameId(), so the game-id check is deferred to SetGameId.
        private string _pendingLinkGameId = "";
        // Fresh-account nickname preservation: when device-code linking CREATES a
        // new account (isNewUser) while the player was anonymous, the account is
        // born with a server-generated name ("Player_2") and migration then stamps
        // it over the name the player actually chose. This holds the anon nickname
        // so it can be restored right after OnAccountUpgraded. Empty = nothing to
        // restore.
        private string _pendingNicknameRestore = "";

        // ============================================================
        // REQUEST QUEUE
        // ============================================================

        private string _currentEndpoint = "";
        private Dictionary<string, object> _currentMeta = new Dictionary<string, object>();
        private bool _httpBusy = false;
        private Queue<RequestData> _requestQueue = new Queue<RequestData>();

        // Deferred achievement tracking (sent after score succeeds)
        private List<string> _deferredAchievementIds = new List<string>();
        private int _deferredAchievementsRemaining = 0;
        private List<string> _deferredAchievementsSynced = new List<string>();
        // Ids sent in the most recent batch request. On HTTP 200 the server
        // has persisted them, so if the response body can't be parsed these
        // are what gets reported — never "0 synced" on a success.
        private List<string> _lastBatchIds = new List<string>();

        // ============================================================
        // PERSISTENT DEVICE ID
        // ============================================================

        private const string DEVICE_ID_PREF_KEY = "cheddaboards_device_id";
        private const string DEVICE_ID_CREATED_KEY = "cheddaboards_device_created";
        private const string SESSION_TOKEN_PREF_KEY = "cheddaboards_session_token";
        private const string SESSION_NICKNAME_PREF_KEY = "cheddaboards_session_nickname";
        private const string SESSION_AUTH_TYPE_PREF_KEY = "cheddaboards_session_auth_type";
        private const string SESSION_SAVED_AT_PREF_KEY = "cheddaboards_session_saved_at";
        // Pending device code (v2.3.0) - survives an app restart / page reload mid-link
        private const string PENDING_LINK_CODE_KEY = "cheddaboards_pending_device_code";
        private const string PENDING_LINK_USER_CODE_KEY = "cheddaboards_pending_user_code";
        private const string PENDING_LINK_URL_KEY = "cheddaboards_pending_link_url";
        private const string PENDING_LINK_QR_KEY = "cheddaboards_pending_link_qr";
        private const string PENDING_LINK_EXPIRES_KEY = "cheddaboards_pending_link_expires";
        private const string PENDING_LINK_INTERVAL_KEY = "cheddaboards_pending_link_interval";
        private const string PENDING_LINK_GAME_KEY = "cheddaboards_pending_link_game";

        // ============================================================
        // INTERNAL TYPES
        // ============================================================

        private class RequestData
        {
            public string endpoint;
            public string method;
            public Dictionary<string, object> body;
            public string requestType;
            public Dictionary<string, object> meta;
            public bool wentDirect = false;
            public bool directAttempted = false;
        }

        // ============================================================
        // INITIALIZATION
        // ============================================================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Log($"Initializing CheddaBoards v{VERSION} (HTTP API Mode)...");
            LoadSavedSession();
            // Resume an interrupted device code link (reload mid-link). Runs
            // synchronously so a login screen that calls LoginWithDeviceCode()
            // on OnSdkReady already sees the saved code. The poll loop's first
            // tick is one interval away, so scenes have time to subscribe.
            RestorePendingLink();
            _initComplete = true;
            StartCoroutine(EmitSdkReadyDeferred());
        }

        private IEnumerator EmitSdkReadyDeferred()
        {
            yield return null; // Wait one frame (equivalent to call_deferred)
            OnSdkReady?.Invoke();
        }

        // ============================================================
        // HTTP HELPERS
        // ============================================================

        /// <summary>Build headers for an HTTP request based on auth state.</summary>
        private Dictionary<string, string> BuildHeaders(string requestType = "")
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            };

            // Session token takes priority over API key (mutually exclusive)
            // EXCEPT: play sessions always use API key (game-level operation, skip_validation)
            bool forceApiKey = (requestType == "start_play_session" || requestType == "end_play_session")
                               && string.IsNullOrEmpty(_sessionToken);
            if (!string.IsNullOrEmpty(_sessionToken) && !forceApiKey)
            {
                headers["X-Session-Token"] = _sessionToken;
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                headers["X-API-Key"] = apiKey;
            }

            if (!string.IsNullOrEmpty(gameId))
            {
                headers["X-Game-ID"] = gameId;
            }

            return headers;
        }

        // ============================================================
        // HTTP REQUEST EXECUTION
        // ============================================================

        private void MakeHttpRequest(string endpoint, string method, Dictionary<string, object> body,
                                     string requestType, Dictionary<string, object> meta = null)
        {
            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(_sessionToken))
            {
                Log($"No credentials set - skipping HTTP request to {endpoint}");
                switch (requestType)
                {
                    case "submit_score":
                    case "submit_score_to_board": OnScoreError?.Invoke("No credentials set"); break;
                    case "player_profile": OnNoProfile?.Invoke(); break;
                    case "leaderboard": OnLeaderboardLoaded?.Invoke(new List<object>()); break;
                    case "player_rank": OnRankError?.Invoke("No credentials set"); break;
                    case "list_scoreboards":
                    case "get_scoreboard":
                        OnScoreboardError?.Invoke("No credentials set"); break;
                    case "list_archives":
                    case "get_archive":
                    case "get_last_archive":
                    case "archive_stats":
                        OnArchiveError?.Invoke("No credentials set"); break;
                }
                return;
            }

            var requestData = new RequestData
            {
                endpoint = endpoint,
                method = method,
                body = body ?? new Dictionary<string, object>(),
                requestType = requestType,
                meta = meta ?? new Dictionary<string, object>()
            };

            if (_httpBusy)
            {
                Log($"HTTP busy, queuing request: {requestType}");
                _requestQueue.Enqueue(requestData);
                return;
            }

            ExecuteHttpRequest(requestData);
        }

        private void ExecuteHttpRequest(RequestData requestData)
        {
            _httpBusy = true;
            _currentEndpoint = requestData.requestType;
            _currentMeta = requestData.meta ?? new Dictionary<string, object>();

            StartCoroutine(SendHttpRequest(requestData));
        }

        // A direct canister read failed at transport/gateway level. Re-issue the
        // identical request through the proxy, and after MAX_FAILURES consecutive
        // direct failures, stop trying direct for the rest of the session.
        private void RetryViaProxy(RequestData requestData, string reason)
        {
            _directReadFailures++;
            if (_directReadFailures == DIRECT_READ_MAX_FAILURES)
                Log($"Direct reads disabled for this session after {_directReadFailures} failures");
            Log($"Direct read failed ({reason}) - retrying via proxy");

            requestData.directAttempted = true;
            requestData.wentDirect = false;
            _httpBusy = false;
            ExecuteHttpRequest(requestData);   // same request, now forced through the proxy
        }

        private IEnumerator SendHttpRequest(RequestData requestData)
        {
            // Eligible public board reads try the canister first, unless this
            // request already failed direct or direct is disabled this session.
            bool useDirect =
                DIRECT_READ_TYPES.Contains(requestData.requestType)
                && !requestData.directAttempted
                && _directReadFailures < DIRECT_READ_MAX_FAILURES;
            requestData.wentDirect = useDirect;

            // Direct reads are keyless + header-free so web exports stay CORS-simple.
            string url = (useDirect ? DIRECT_READ_URL : API_BASE_URL) + requestData.endpoint;
            var headers = useDirect
                ? new Dictionary<string, string>()
                : BuildHeaders(_currentEndpoint);

            string methodStr = requestData.method;
            Log($"HTTP {methodStr}: {url}");

            UnityWebRequest request;
            string jsonBody = requestData.body.Count > 0 ? DictToJson(requestData.body) : "";

            switch (requestData.method)
            {
                case "POST":
                    request = new UnityWebRequest(url, "POST");
                    if (!string.IsNullOrEmpty(jsonBody))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                case "PUT":
                    request = new UnityWebRequest(url, "PUT");
                    if (!string.IsNullOrEmpty(jsonBody))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                case "DELETE":
                    request = UnityWebRequest.Delete(url);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;
                default: // GET
                    request = UnityWebRequest.Get(url);
                    break;
            }

            foreach (var header in headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                // A direct canister read that fails at the network level (or
                // with a gateway 5xx) falls back to the proxy with the identical
                // request. A 4xx from the canister is a real answer (e.g. 404
                // board not found) and is NOT retried - UnityWebRequest reports
                // every 4xx as ProtocolError, so check the status code too.
                if (requestData.wentDirect &&
                    (request.result == UnityWebRequest.Result.ConnectionError ||
                     request.responseCode >= 500 || request.responseCode == 0))
                {
                    request.Dispose();
                    RetryViaProxy(requestData, "network");
                    yield break;
                }

                // Network-level errors
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.LogError($"[CheddaBoards] Request failed: network error");
                    OnRequestFailed?.Invoke(_currentEndpoint, "Network error");
                    EmitHttpFailure("Network error");
                    request.Dispose();
                    yield break;
                }
            }

            string responseText = request.downloadHandler?.text ?? "";
            long responseCode = request.responseCode;
            request.Dispose();

            // A direct canister read that returns a gateway error (5xx, or a
            // non-JSON error page) falls back to the proxy with the same request.
            if (requestData.wentDirect && (responseCode >= 500 || responseCode == 0))
            {
                RetryViaProxy(requestData, $"gateway {responseCode}");
                yield break;
            }

            var response = ParseJson(responseText) as Dictionary<string, object>;
            if (response == null)
            {
                if (requestData.wentDirect)
                {
                    RetryViaProxy(requestData, "parse");
                    yield break;
                }
                string firstChars = "n/a";
                if (!string.IsNullOrEmpty(responseText))
                {
                    var codes = new List<string>();
                    for (int ci = 0; ci < Math.Min(3, responseText.Length); ci++)
                        codes.Add(((int)responseText[ci]).ToString());
                    firstChars = string.Join(",", codes);
                }
                Debug.LogError($"[CheddaBoards] Failed to parse JSON response (HTTP {responseCode}, {(responseText == null ? "null" : responseText.Length.ToString())} chars, first char codes: {firstChars}): {responseText}");
                OnRequestFailed?.Invoke(_currentEndpoint, "Invalid JSON response");
                EmitHttpFailure("Invalid JSON response");
                yield break;
            }

            if (responseCode != 200)
            {
                string errorMsg = GetString(response, "error", "Unknown error");

                // Server rejected our session token - expire it so the game
                // falls back to the login screen instead of erroring forever
                if ((responseCode == 401 || responseCode == 403) && !string.IsNullOrEmpty(_sessionToken))
                {
                    ExpireSession();
                    EmitHttpFailure(errorMsg);
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // 404 on profile lookup is expected for new players
                if (responseCode == 404 && _currentEndpoint == "player_profile")
                {
                    Log("Player profile not found (new player) - normal for first-time players");
                    _isRefreshingProfile = false;
                    OnNoProfile?.Invoke();
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // 404 on end play session - already consumed or expired
                if (responseCode == 404 && _currentEndpoint == "end_play_session")
                {
                    Log("Play session already ended or expired - normal");
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // Migration errors are non-fatal
                if (_currentEndpoint == "migrate_account")
                {
                    Log($"Migration note: {errorMsg} (non-fatal, continuing)");
                    _pendingNicknameRestore = "";
                    OnAccountUpgradeFailed?.Invoke(errorMsg);
                    _currentMeta = new Dictionary<string, object>();
                    _httpBusy = false;
                    ProcessNextRequest();
                    yield break;
                }

                // 404 on scoreboard lookup - scoreboard not configured for this game (non-fatal)
                if (responseCode == 404 &&
                    (_currentEndpoint == "get_scoreboard" || _currentEndpoint == "scoreboard_rank" || _currentEndpoint == "list_scoreboards"))
                {
                    Log("Scoreboard not found (404) - may not be configured for this game");
                    EmitHttpFailure(errorMsg);
                    yield break;
                }

                Debug.LogError($"[CheddaBoards] API error ({responseCode}): {errorMsg}");
                OnRequestFailed?.Invoke(_currentEndpoint, errorMsg);
                EmitHttpFailure(errorMsg);
                yield break;
            }

            bool ok = false;
            if (response.ContainsKey("ok"))
            {
                var okVal = response["ok"];
                if (okVal is bool b) ok = b;
                else if (okVal is string s) ok = s.ToLower() == "true";
            }

            if (!ok)
            {
                string errorMsg = GetString(response, "error", "Unknown error");
                OnRequestFailed?.Invoke(_currentEndpoint, errorMsg);
                EmitHttpFailure(errorMsg);
                yield break;
            }

            var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : new Dictionary<string, object>();
            if (data == null) data = new Dictionary<string, object>();

            // A clean direct read succeeded — reset the session failure counter.
            if (requestData.wentDirect)
                _directReadFailures = 0;

            EmitHttpSuccess(data);
        }

        /// <summary>Fire-and-forget HTTP request (non-blocking, doesn't use queue).
        /// Used for achievements so they don't block leaderboard loading.</summary>
        private void MakeHttpRequestAsync(string endpoint, string method, Dictionary<string, object> body, string requestType)
        {
            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(_sessionToken))
            {
                Log($"No credentials - skipping async request to {endpoint}");
                return;
            }

            StartCoroutine(SendHttpRequestAsync(endpoint, method, body, requestType));
        }

        private IEnumerator SendHttpRequestAsync(string endpoint, string method, Dictionary<string, object> body, string requestType)
        {
            var headers = BuildHeaders(requestType);
            string url = API_BASE_URL + endpoint;
            string jsonBody = body != null && body.Count > 0 ? DictToJson(body) : "";

            Log($"HTTP async {requestType}: {endpoint}");

            UnityWebRequest request;
            if (method == "POST")
            {
                request = new UnityWebRequest(url, "POST");
                if (!string.IsNullOrEmpty(jsonBody))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                }
                request.downloadHandler = new DownloadHandlerBuffer();
            }
            else
            {
                request = UnityWebRequest.Get(url);
            }

            foreach (var header in headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }

            yield return request.SendWebRequest();

            long code = request.responseCode;
            string responseText = request.downloadHandler?.text ?? "";
            request.Dispose();

            if (code >= 200 && code < 300)
            {
                Log($"Async {requestType} complete (HTTP {code})");

                // Handle async achievement responses
                if (requestType == "unlock_achievement")
                {
                    var resp = ParseJson(responseText) as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("data"))
                    {
                        var asyncData = resp["data"] as Dictionary<string, object>;
                        if (asyncData != null)
                        {
                            string achId = GetString(asyncData, "achievementId", "");
                            OnAchievementUnlocked?.Invoke(achId);
                            if (_deferredAchievementsRemaining > 0)
                            {
                                _deferredAchievementsSynced.Add(achId);
                                _deferredAchievementsRemaining--;
                                Log($"Achievement synced: {achId} ({_deferredAchievementsRemaining} remaining)");
                                if (_deferredAchievementsRemaining <= 0)
                                {
                                    Log($"All deferred achievements done: {_deferredAchievementsSynced.Count} synced");
                                    OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                                    _deferredAchievementsSynced.Clear();
                                }
                            }
                        }
                    }
                }
                else if (requestType == "unlock_achievement_batch")
                {
                    var resp = ParseJson(responseText) as Dictionary<string, object>;
                    var asyncData = (resp != null && resp.ContainsKey("data"))
                        ? resp["data"] as Dictionary<string, object>
                        : null;

                    var syncedIds = ParseBatchSyncedIds(asyncData);
                    if (syncedIds.Count == 0 && _lastBatchIds.Count > 0)
                    {
                        // HTTP 200 means the server persisted the batch even if
                        // the body shape isn't one we recognise — report the
                        // requested ids rather than a false "0 synced".
                        Log("Batch response shape unrecognised; reporting requested ids as synced. Raw: " + responseText);
                        syncedIds = new List<string>(_lastBatchIds);
                    }

                    Log($"Batch achievement sync complete: {syncedIds.Count} synced");
                    foreach (string achId in syncedIds)
                    {
                        _deferredAchievementsSynced.Add(achId);
                        OnAchievementUnlocked?.Invoke(achId);
                    }
                    OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    _lastBatchIds.Clear();
                }
            }
            else
            {
                Log($"Async {requestType} failed (HTTP {code})");

                if (requestType == "unlock_achievement" && _deferredAchievementsRemaining > 0)
                {
                    _deferredAchievementsRemaining--;
                    Log($"Achievement unlock failed ({_deferredAchievementsRemaining} remaining)");
                    if (_deferredAchievementsRemaining <= 0)
                    {
                        OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                        _deferredAchievementsSynced.Clear();
                    }
                }
                else if (requestType == "unlock_achievement_batch")
                {
                    Log($"Batch achievement sync failed");
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    _lastBatchIds.Clear();
                }
            }
        }

        // ============================================================
        // HTTP RESPONSE HANDLERS
        // ============================================================

        private void EmitHttpSuccess(Dictionary<string, object> data)
        {
            switch (_currentEndpoint)
            {
                case "submit_score":
                    _isSubmittingScore = false;
                    _playerExistsOnBackend = true;   // submit creates/updates the player
                    // If a local-only rename is pending, the submit body just
                    // carried it (_nickname was set) - the profile fetch that
                    // follows confirms and clears _pendingServerNickname.
                    Log($"Score submission successful: {_pendingScore} points, {_pendingStreak} streak");
                    OnScoreSubmitted?.Invoke(_pendingScore, _pendingStreak);
                    FlushDeferredAchievements();
                    break;

                case "submit_score_to_board":
                {
                    _playerExistsOnBackend = true;
                    string sbId = GetMetaString("scoreboard_id");
                    int sbScore = SafeInt(_currentMeta.ContainsKey("score") ? _currentMeta["score"] : 0);
                    int sbStreak = SafeInt(_currentMeta.ContainsKey("streak") ? _currentMeta["streak"] : 0);
                    Log($"Targeted submit to '{sbId}' successful: {sbScore} points, {sbStreak} streak");
                    OnScoreSubmittedToBoard?.Invoke(sbId, sbScore, sbStreak);
                    break;
                }

                case "leaderboard":
                    var entries = GetList(data, "leaderboard");
                    OnLeaderboardLoaded?.Invoke(entries);
                    break;

                case "player_rank":
                {
                    int rank = SafeInt(data.ContainsKey("rank") ? data["rank"] : 0);
                    int scoreVal = SafeInt(data.ContainsKey("score") ? data["score"] : 0);
                    int streakVal = SafeInt(data.ContainsKey("streak") ? data["streak"] : 0);
                    int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                    OnPlayerRankLoaded?.Invoke(rank, scoreVal, streakVal, total);
                    break;
                }

                case "player_profile":
                    _isRefreshingProfile = false;
                    if (data != null && data.Count > 0)
                        UpdateCachedProfile(data);
                    else
                    {
                        // Distinct from the 404 path: the request SUCCEEDED but
                        // the body carried no profile data. If this fires for a
                        // player who has submitted, the proxy's profile response
                        // shape needs checking, not the player's existence.
                        Log("Profile response OK but empty - treating as no profile");
                        OnNoProfile?.Invoke();
                    }
                    break;

                case "change_nickname":
                {
                    string newNick = GetString(data, "nickname", "");
                    if (newNick == "" && !string.IsNullOrEmpty(_requestedNickname))
                    {
                        // 2xx + ok:true means the rename landed even if the
                        // response doesn't echo the name back. Prefer the
                        // echoed name when present (server may suffix on
                        // collision, e.g. Name_1); otherwise report what we
                        // asked for. The old handler silently did NOTHING
                        // here - no event, no error, no board refresh.
                        Log("Rename response had no nickname field - using requested name");
                        newNick = _requestedNickname;
                    }
                    _requestedNickname = "";
                    if (newNick != "")
                    {
                        _nickname = newNick;
                        _nicknameJustChanged = true;
                        _pendingServerNickname = "";
                        _pendingRenameAttempts = 0;
                        if (_cachedProfile.Count > 0)
                            _cachedProfile["nickname"] = newNick;
                        OnNicknameChanged?.Invoke(newNick);
                        Log($"Nickname changed to: {newNick}");
                    }
                    break;
                }

                case "change_nickname_anonymous":
                {
                    string newNick = GetString(data, "nickname", "");
                    if (newNick == "" && !string.IsNullOrEmpty(_requestedNickname))
                    {
                        Log("Rename response had no nickname field - using requested name");
                        newNick = _requestedNickname;
                    }
                    _requestedNickname = "";
                    if (newNick != "")
                    {
                        _nickname = newNick;
                        _nicknameJustChanged = true;
                        _pendingServerNickname = "";
                        _pendingRenameAttempts = 0;
                        if (_cachedProfile.Count > 0)
                            _cachedProfile["nickname"] = newNick;
                        OnNicknameChanged?.Invoke(newNick);
                        Log($"Anonymous nickname changed to: {newNick}");
                    }
                    GetPlayerProfile();
                    break;
                }

                case "unlock_achievement":
                {
                    string achId = GetString(data, "achievementId", "");
                    OnAchievementUnlocked?.Invoke(achId);
                    if (_deferredAchievementsRemaining > 0)
                    {
                        _deferredAchievementsSynced.Add(achId);
                        _deferredAchievementsRemaining--;
                        Log($"Achievement synced: {achId} ({_deferredAchievementsRemaining} remaining)");
                        if (_deferredAchievementsRemaining <= 0)
                        {
                            Log($"All deferred achievements done: {_deferredAchievementsSynced.Count} synced");
                            OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                            _deferredAchievementsSynced.Clear();
                        }
                    }
                    break;
                }

                case "unlock_achievement_batch":
                {
                    var syncedIds = ParseBatchSyncedIds(data);
                    if (syncedIds.Count == 0 && _lastBatchIds.Count > 0)
                    {
                        Log("Batch response shape unrecognised; reporting requested ids as synced.");
                        syncedIds = new List<string>(_lastBatchIds);
                    }
                    Log($"Batch achievement sync complete: {syncedIds.Count} synced");
                    foreach (string achId in syncedIds)
                    {
                        _deferredAchievementsSynced.Add(achId);
                        OnAchievementUnlocked?.Invoke(achId);
                    }
                    OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    _lastBatchIds.Clear();
                    break;
                }

                case "achievements":
                {
                    // Response is a profile: achievements sit under
                    // gameProfile.achievements. Top-level "achievements" is
                    // kept as a fallback for older/alternate responses.
                    var gp = data.ContainsKey("gameProfile") ? data["gameProfile"] as Dictionary<string, object> : null;
                    var achievements = gp != null ? GetList(gp, "achievements") : new List<object>();
                    if (achievements.Count == 0)
                        achievements = GetList(data, "achievements");
                    OnAchievementsLoaded?.Invoke(achievements);
                    break;
                }

                case "list_scoreboards":
                {
                    var scoreboards = GetList(data, "scoreboards");
                    OnScoreboardsLoaded?.Invoke(scoreboards);
                    Log($"Loaded {scoreboards.Count} scoreboards");
                    break;
                }

                case "get_scoreboard":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    var config = data.ContainsKey("config") ? data["config"] as Dictionary<string, object> : new Dictionary<string, object>();
                    var sbEntries = GetList(data, "entries");
                    OnScoreboardLoaded?.Invoke(sbId, config ?? new Dictionary<string, object>(), sbEntries);
                    Log($"Loaded scoreboard '{sbId}' with {sbEntries.Count} entries");
                    break;
                }

                case "scoreboard_rank":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    bool found = data.ContainsKey("found") && data["found"] is bool fb && fb;
                    if (found)
                    {
                        int rank = SafeInt(data.ContainsKey("rank") ? data["rank"] : 0);
                        int scoreVal = SafeInt(data.ContainsKey("score") ? data["score"] : 0);
                        int streakVal = SafeInt(data.ContainsKey("streak") ? data["streak"] : 0);
                        int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                        OnScoreboardRankLoaded?.Invoke(sbId, rank, scoreVal, streakVal, total);
                    }
                    else
                    {
                        int total = SafeInt(data.ContainsKey("totalPlayers") ? data["totalPlayers"] : 0);
                        OnScoreboardRankLoaded?.Invoke(sbId, 0, 0, 0, total);
                    }
                    break;
                }

                case "list_archives":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    var archives = GetList(data, "archives");
                    OnArchivesListLoaded?.Invoke(sbId, archives);
                    Log($"Loaded {archives.Count} archives for '{sbId}'");
                    break;
                }

                case "get_archive":
                case "get_last_archive":
                {
                    string archiveId = GetString(data, "archiveId", GetMetaString("archive_id"));
                    var config = data.ContainsKey("config") ? data["config"] as Dictionary<string, object> : new Dictionary<string, object>();
                    var archEntries = GetList(data, "entries");
                    OnArchivedScoreboardLoaded?.Invoke(archiveId, config ?? new Dictionary<string, object>(), archEntries);
                    Log($"Loaded archive '{archiveId}' with {archEntries.Count} entries");
                    break;
                }

                case "archive_stats":
                {
                    int totalArchives = SafeInt(data.ContainsKey("totalArchives") ? data["totalArchives"] : 0);
                    var bySb = GetList(data, "byScoreboard");
                    OnArchiveStatsLoaded?.Invoke(totalArchives, bySb);
                    Log($"Archive stats: {totalArchives} total archives");
                    break;
                }

                case "game_info":
                case "game_stats":
                case "health":
                    Log($"API response: {DictToJson(data)}");
                    break;

                case "start_play_session":
                    if (data.ContainsKey("ok"))
                        _playSessionToken = data["ok"].ToString();
                    else if (data.ContainsKey("token"))
                        _playSessionToken = data["token"].ToString();
                    else
                    {
                        string err = data.ContainsKey("err") ? data["err"].ToString()
                                   : data.ContainsKey("error") ? data["error"].ToString()
                                   : "Unknown error";
                        Log($"Play session error: {err}");
                        OnPlaySessionError?.Invoke(err);
                        _currentMeta = new Dictionary<string, object>();
                        _httpBusy = false;
                        ProcessNextRequest();
                        return;
                    }
                    Log($"Play session started: {_playSessionToken.Substring(0, Math.Min(30, _playSessionToken.Length))}");
                    OnPlaySessionStarted?.Invoke(_playSessionToken);
                    break;

                case "end_play_session":
                    Log("Play session ended on server successfully");
                    break;

                case "migrate_account":
                {
                    int migratedGames = SafeInt(data.ContainsKey("migratedGames") ? data["migratedGames"] : 0);
                    int migratedSb = SafeInt(data.ContainsKey("migratedScoreboards") ? data["migratedScoreboards"] : 0);
                    Log($"Migration complete: {migratedGames} games, {migratedSb} scoreboards migrated");
                    RefreshProfile();
                    OnAccountUpgraded?.Invoke(_cachedProfile, new Dictionary<string, object>
                    {
                        { "migratedGames", migratedGames },
                        { "migratedScoreboards", migratedSb }
                    });
                    // Fresh-account case: put the player's chosen anon name back on the
                    // new account (server validates; suffixes on collision). No-op for
                    // merges into existing accounts (_pendingNicknameRestore empty).
                    if (!string.IsNullOrEmpty(_pendingNicknameRestore))
                    {
                        string restoreNick = _pendingNicknameRestore;
                        _pendingNicknameRestore = "";
                        Log($"Restoring player-chosen nickname on new account: {restoreNick}");
                        ChangeNickname(restoreNick);
                    }
                    break;
                }

                case "device_code_request":
                {
                    string dc = GetString(data, "device_code", "");
                    string uc = GetString(data, "user_code", "");
                    string urlVal = GetString(data, "verification_url", "");
                    string urlComplete = GetString(data, "verification_url_complete", "");
                    int expiresIn = SafeInt(data.ContainsKey("expires_in") ? data["expires_in"] : 300);
                    int interval = SafeInt(data.ContainsKey("interval") ? data["interval"] : 5);
                    string qrDataUrl = GetString(data, "qr_data_url", "");

                    _deviceCode = dc;
                    _deviceUserCode = uc;
                    _deviceCodePollInterval = interval;
                    _deviceCodeExpiresAt = GetUnixTime() + expiresIn;
                    _deviceVerificationUrl = !string.IsNullOrEmpty(urlComplete) ? urlComplete : urlVal;
                    _deviceQrDataUrl = qrDataUrl;
                    _pendingLinkGameId = gameId;
                    SavePendingLink();

                    Log($"Device code received: {RedactCode(uc)} (expires in {expiresIn}s)");
                    OnDeviceCodeReceived?.Invoke(uc, _deviceVerificationUrl, _deviceQrDataUrl);
                    StartDeviceCodePolling();
                    break;
                }

                case "device_code_token":
                    // Handled in custom polling function
                    break;
            }

            _currentMeta = new Dictionary<string, object>();
            _httpBusy = false;
            ProcessNextRequest();
        }

        private void EmitHttpFailure(string error)
        {
            switch (_currentEndpoint)
            {
                case "submit_score":
                    _isSubmittingScore = false;
                    OnScoreError?.Invoke(error);
                    break;
                case "submit_score_to_board":
                    Log($"Targeted submit to '{GetMetaString("scoreboard_id")}' failed: {error}");
                    OnScoreError?.Invoke(error);
                    break;
                case "leaderboard":
                    OnLeaderboardLoaded?.Invoke(new List<object>());
                    break;
                case "player_rank":
                    OnRankError?.Invoke(error);
                    break;
                case "player_profile":
                    _isRefreshingProfile = false;
                    OnNoProfile?.Invoke();
                    break;
                case "change_nickname":
                case "change_nickname_anonymous":
                    _requestedNickname = "";
                    OnNicknameError?.Invoke(error);
                    break;
                case "unlock_achievement":
                    if (_deferredAchievementsRemaining > 0)
                    {
                        _deferredAchievementsRemaining--;
                        Log($"Achievement unlock failed ({_deferredAchievementsRemaining} remaining)");
                        if (_deferredAchievementsRemaining <= 0)
                        {
                            OnAchievementsLoaded?.Invoke(new List<object>(_deferredAchievementsSynced));
                            _deferredAchievementsSynced.Clear();
                        }
                    }
                    break;
                case "unlock_achievement_batch":
                    Log($"Batch achievement sync failed: {error}");
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    _deferredAchievementsSynced.Clear();
                    _deferredAchievementsRemaining = 0;
                    _lastBatchIds.Clear();
                    break;
                case "achievements":
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    break;
                case "list_scoreboards":
                    OnScoreboardsLoaded?.Invoke(new List<object>());
                    OnScoreboardError?.Invoke(error);
                    break;
                case "get_scoreboard":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnScoreboardLoaded?.Invoke(sbId, new Dictionary<string, object>(), new List<object>());
                    OnScoreboardError?.Invoke(error);
                    break;
                }
                case "scoreboard_rank":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnScoreboardRankLoaded?.Invoke(sbId, 0, 0, 0, 0);
                    OnScoreboardError?.Invoke(error);
                    break;
                }
                case "list_archives":
                {
                    string sbId = GetMetaString("scoreboard_id");
                    OnArchivesListLoaded?.Invoke(sbId, new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
                }
                case "get_archive":
                case "get_last_archive":
                {
                    string archiveId = GetMetaString("archive_id");
                    OnArchivedScoreboardLoaded?.Invoke(archiveId, new Dictionary<string, object>(), new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
                }
                case "start_play_session":
                    Log($"Play session error: {error}");
                    OnPlaySessionError?.Invoke(error);
                    break;
                case "end_play_session":
                    Log($"End play session error (ignored): {error}");
                    break;
                case "migrate_account":
                    Log($"Migration failed: {error}");
                    _pendingNicknameRestore = "";
                    OnAccountUpgradeFailed?.Invoke(error);
                    break;
                case "device_code_request":
                    Log($"Device code request failed: {error}");
                    OnDeviceCodeError?.Invoke(error);
                    break;
                case "archive_stats":
                    OnArchiveStatsLoaded?.Invoke(0, new List<object>());
                    OnArchiveError?.Invoke(error);
                    break;
            }

            _currentMeta = new Dictionary<string, object>();
            _httpBusy = false;
            ProcessNextRequest();
        }

        private void ProcessNextRequest()
        {
            if (_requestQueue.Count == 0) return;

            var next = _requestQueue.Dequeue();
            Log($"Processing queued request: {next.requestType}");
            ExecuteHttpRequest(next);
        }

        // ============================================================
        // PROFILE MANAGEMENT
        // ============================================================

        private void UpdateCachedProfile(Dictionary<string, object> profile)
        {
            if (profile == null || profile.Count == 0) return;

            _playerExistsOnBackend = true;   // a profile loaded, so the player exists

            // Preserve nickname from recent rename - backend may return stale data
            if (_nicknameJustChanged && !string.IsNullOrEmpty(_nickname))
            {
                profile["nickname"] = _nickname;
                Log($"Preserving renamed nickname '{_nickname}' over stale backend data");
                _nicknameJustChanged = false;
            }

            // A name set locally BEFORE the player existed must win over the
            // server's empty/old name, and now that a rename can land, push it.
            // Capped so a persistently rejected name can't loop forever.
            if (!string.IsNullOrEmpty(_pendingServerNickname))
            {
                string serverNick = GetString(profile, "nickname",
                                    GetString(profile, "username", ""));
                if (serverNick == _pendingServerNickname)
                {
                    // First submit carried it - all synced.
                    _pendingServerNickname = "";
                    _pendingRenameAttempts = 0;
                }
                else
                {
                    profile["nickname"] = _pendingServerNickname;
                    if (_pendingRenameAttempts < 2)
                    {
                        _pendingRenameAttempts++;
                        Log($"Re-syncing locally set nickname '{_pendingServerNickname}' to backend (attempt {_pendingRenameAttempts})");
                        ChangeNickname(_pendingServerNickname);
                    }
                }
            }

            _cachedProfile = new Dictionary<string, object>(profile);

            // Unnamed profile stays unnamed. Backfilling GetDefaultNickname() here
            // would set _nickname to a generated name, and the next submit would
            // write it to the server — reintroducing the rename bug via the read path.
            string nickname = GetString(profile, "nickname",
                              GetString(profile, "username", ""));

            // Handle nested gameProfile from API
            var gameProfile = profile.ContainsKey("gameProfile") ? profile["gameProfile"] as Dictionary<string, object> : null;
            int score = 0;
            int streak = 0;
            List<object> achievements = new List<object>();
            int playCount = 0;

            if (gameProfile != null && gameProfile.Count > 0)
            {
                score = SafeInt(gameProfile.ContainsKey("score") ? gameProfile["score"] : 0);
                streak = SafeInt(gameProfile.ContainsKey("streak") ? gameProfile["streak"] : 0);
                achievements = GetList(gameProfile, "achievements");
                playCount = SafeInt(gameProfile.ContainsKey("playCount") ? gameProfile["playCount"] : 0);
                _cachedProfile["score"] = score;
                _cachedProfile["streak"] = streak;
                _cachedProfile["achievements"] = achievements;
                _cachedProfile["playCount"] = playCount;
            }
            else
            {
                score = SafeInt(profile.ContainsKey("score") ? profile["score"]
                      : profile.ContainsKey("highScore") ? profile["highScore"] : 0);
                streak = SafeInt(profile.ContainsKey("streak") ? profile["streak"]
                       : profile.ContainsKey("bestStreak") ? profile["bestStreak"] : 0);
                achievements = GetList(profile, "achievements");
                playCount = SafeInt(profile.ContainsKey("playCount") ? profile["playCount"]
                          : profile.ContainsKey("plays") ? profile["plays"] : 0);
            }

            _nickname = nickname;
            OnProfileLoaded?.Invoke(nickname, score, streak, achievements, playCount);
        }

        // ============================================================
        // LOGGING
        // ============================================================

        private void Log(string message)
        {
            if (debugLogging)
                Debug.Log($"[CheddaBoards] {message}");
        }

        // Redact a user-facing device code for logging. Shows the first 3 chars so
        // a developer can still correlate logs with what's on screen, without
        // revealing the full code an attacker would brute-force during approval.
        private string RedactCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length <= 3) return "***";
            return code.Substring(0, 3) + "***";
        }

        // Redact an email for logging. Keeps the first character of the local part
        // and the full domain so developers can tell which user without exposing
        // the full address.
        private string RedactEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return "(none)";
            int at = email.IndexOf('@');
            if (at <= 1) return at > 0 ? "***" + email.Substring(at) : "***";
            return email.Substring(0, 1) + "***" + email.Substring(at);
        }

        // ============================================================
        // PUBLIC API - UTILITIES
        // ============================================================

        public bool IsReady() => _initComplete;

        public bool CanConnect() => _initComplete && (!string.IsNullOrEmpty(apiKey) || !string.IsNullOrEmpty(_sessionToken));

        /// <summary>Safely convert any value to int. Handles null, float, string, etc.</summary>
        public static int SafeInt(object value)
        {
            if (value == null) return 0;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is float f) return (int)f;
            if (value is double d) return (int)d;
            if (value is string s)
            {
                if (int.TryParse(s, out int parsed)) return parsed;
                if (float.TryParse(s, out float fparsed)) return (int)fparsed;
                return 0;
            }
            return 0;
        }

        private string GetDefaultNickname()
        {
            string pid = GetPlayerId();
            return "Player_" + (pid.Length > 6 ? pid.Substring(0, 6) : pid);
        }

        public string GetNickname()
        {
            if (!string.IsNullOrEmpty(_nickname)
                && !_nickname.StartsWith("Player_p_") && !_nickname.StartsWith("Player_dev_"))
                return _nickname;

            if (_cachedProfile.Count > 0)
            {
                string profileNick = GetString(_cachedProfile, "nickname", "");
                if (!string.IsNullOrEmpty(profileNick)
                    && !profileNick.StartsWith("Player_p_") && !profileNick.StartsWith("Player_dev_"))
                    return profileNick;
            }

            // Return empty — callers should show "Guest" for unnamed anonymous players.
            return "";
        }

        public int GetHighScore()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("score"))
                return SafeInt(_cachedProfile["score"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("score") ? gp["score"] : 0);
            return 0;
        }

        public int GetBestStreak()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("streak"))
                return SafeInt(_cachedProfile["streak"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("streak") ? gp["streak"] : 0);
            return 0;
        }

        public int GetPlayCount()
        {
            if (_cachedProfile.Count == 0) return 0;
            if (_cachedProfile.ContainsKey("playCount"))
                return SafeInt(_cachedProfile["playCount"]);
            var gp = _cachedProfile.ContainsKey("gameProfile") ? _cachedProfile["gameProfile"] as Dictionary<string, object> : null;
            if (gp != null && gp.Count > 0)
                return SafeInt(gp.ContainsKey("playCount") ? gp["playCount"] : 0);
            return 0;
        }

        public Dictionary<string, object> GetCachedProfile() => new Dictionary<string, object>(_cachedProfile);

        public string GetAuthType() => _authType;

        // ============================================================
        // PUBLIC API - CONFIGURATION
        // ============================================================

        public void SetApiKey(string key)
        {
            apiKey = key;
            Log("API key set");
        }

        public void SetGameId(string id)
        {
            gameId = id;
            Log($"Game ID set: {id}");
            ReconcilePendingLinkWithGameId();
        }

        public void SetSessionToken(string token)
        {
            _sessionToken = token;
            Log("Session token set");
            SaveSession();
        }

        public void SetPlayerId(string playerId)
        {
            _playerId = SanitizePlayerId(playerId);
            Log($"Player ID set: {_playerId}");
        }

        public string GetPlayerId()
        {
            if (!string.IsNullOrEmpty(_playerId))
                return _playerId;

            // Try loading saved device ID from PlayerPrefs
            string savedId = LoadDeviceId();
            if (!string.IsNullOrEmpty(savedId))
            {
                _playerId = savedId;
                Log($"Loaded persistent device ID: {_playerId.Substring(0, Math.Min(12, _playerId.Length))}");
                return _playerId;
            }

            // Generate new persistent device ID (first launch only)
            string timestamp = ((long)(GetUnixTime() * 1000)).ToString();
            string randomPart = UnityEngine.Random.Range(0, int.MaxValue).ToString("x8");
            _playerId = "dev_" + timestamp + "_" + randomPart;
            SaveDeviceId(_playerId);
            Log($"Generated new persistent device ID: {_playerId}");
            return _playerId;
        }

        private void SaveDeviceId(string deviceId)
        {
            PlayerPrefs.SetString(DEVICE_ID_PREF_KEY, deviceId);
            PlayerPrefs.SetFloat(DEVICE_ID_CREATED_KEY, (float)GetUnixTime());
            PlayerPrefs.Save();
            Log($"Device ID saved to PlayerPrefs");
        }

        private string LoadDeviceId()
        {
            return PlayerPrefs.GetString(DEVICE_ID_PREF_KEY, "");
        }

        // ============================================================
        // SESSION PERSISTENCE (v2.2.3)
        // ============================================================
        // The device-code session token is saved so players stay signed
        // in across restarts. The stored token is validated lazily: it is
        // restored optimistically and cleared if the server rejects it
        // (401/403 -> OnSessionExpired).

        private void SaveSession()
        {
            if (string.IsNullOrEmpty(_sessionToken))
                return;
            PlayerPrefs.SetString(SESSION_TOKEN_PREF_KEY, _sessionToken);
            PlayerPrefs.SetString(SESSION_NICKNAME_PREF_KEY, _nickname);
            PlayerPrefs.SetString(SESSION_AUTH_TYPE_PREF_KEY, _authType);
            PlayerPrefs.SetFloat(SESSION_SAVED_AT_PREF_KEY, (float)GetUnixTime());
            PlayerPrefs.Save();
            Log("Session saved to PlayerPrefs");
        }

        private void LoadSavedSession()
        {
            string token = PlayerPrefs.GetString(SESSION_TOKEN_PREF_KEY, "");
            if (string.IsNullOrEmpty(token))
                return;
            _sessionToken = token;
            _nickname = PlayerPrefs.GetString(SESSION_NICKNAME_PREF_KEY, "");
            _authType = PlayerPrefs.GetString(SESSION_AUTH_TYPE_PREF_KEY, "google");
            _cachedProfile = new Dictionary<string, object> { { "nickname", _nickname } };
            Log($"Restored saved session for {_nickname} (validated on first request)");
        }

        private void ClearSavedSession()
        {
            if (!PlayerPrefs.HasKey(SESSION_TOKEN_PREF_KEY))
                return;
            PlayerPrefs.DeleteKey(SESSION_TOKEN_PREF_KEY);
            PlayerPrefs.DeleteKey(SESSION_NICKNAME_PREF_KEY);
            PlayerPrefs.DeleteKey(SESSION_AUTH_TYPE_PREF_KEY);
            PlayerPrefs.DeleteKey(SESSION_SAVED_AT_PREF_KEY);
            PlayerPrefs.Save();
            Log("Saved session cleared");
        }

        // ============================================================
        // PENDING DEVICE CODE PERSISTENCE (v2.3.0)
        // ============================================================
        // On phones / WebGL home-screen apps the game can be reloaded while
        // the player is off approving the code in their browser. Without
        // this the code lived only in memory, the login screen minted a new
        // one on reload, and every approval hit a code the SDK had already
        // dropped.

        private void SavePendingLink()
        {
            if (string.IsNullOrEmpty(_deviceCode))
                return;
            PlayerPrefs.SetString(PENDING_LINK_CODE_KEY, _deviceCode);
            PlayerPrefs.SetString(PENDING_LINK_USER_CODE_KEY, _deviceUserCode);
            PlayerPrefs.SetString(PENDING_LINK_URL_KEY, _deviceVerificationUrl);
            PlayerPrefs.SetString(PENDING_LINK_QR_KEY, _deviceQrDataUrl);
            // Stored as a string: PlayerPrefs.SetFloat is single precision and
            // loses whole seconds at unix-time magnitudes.
            PlayerPrefs.SetString(PENDING_LINK_EXPIRES_KEY, _deviceCodeExpiresAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.SetFloat(PENDING_LINK_INTERVAL_KEY, _deviceCodePollInterval);
            PlayerPrefs.SetString(PENDING_LINK_GAME_KEY, gameId ?? "");
            PlayerPrefs.Save();
            Log($"Pending device code saved ({RedactCode(_deviceUserCode)})");
        }

        private void ClearPendingLink()
        {
            if (!PlayerPrefs.HasKey(PENDING_LINK_CODE_KEY))
                return;
            PlayerPrefs.DeleteKey(PENDING_LINK_CODE_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_USER_CODE_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_URL_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_QR_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_EXPIRES_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_INTERVAL_KEY);
            PlayerPrefs.DeleteKey(PENDING_LINK_GAME_KEY);
            PlayerPrefs.Save();
            Log("Pending device code cleared");
        }

        private void RestorePendingLink()
        {
            if (!PlayerPrefs.HasKey(PENDING_LINK_CODE_KEY))
                return;
            // Already signed in (session restored) - a leftover code is stale.
            if (!string.IsNullOrEmpty(_sessionToken))
            {
                ClearPendingLink();
                return;
            }
            string dc = PlayerPrefs.GetString(PENDING_LINK_CODE_KEY, "");
            double expiresAt = 0;
            double.TryParse(PlayerPrefs.GetString(PENDING_LINK_EXPIRES_KEY, "0"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out expiresAt);
            string savedGame = PlayerPrefs.GetString(PENDING_LINK_GAME_KEY, "");
            if (string.IsNullOrEmpty(dc) || GetUnixTime() >= expiresAt)
            {
                Log("Saved device code has expired - discarding");
                ClearPendingLink();
                return;
            }
            _pendingLinkGameId = savedGame;
            _deviceCode = dc;
            _deviceUserCode = PlayerPrefs.GetString(PENDING_LINK_USER_CODE_KEY, "");
            _deviceVerificationUrl = PlayerPrefs.GetString(PENDING_LINK_URL_KEY, "");
            _deviceQrDataUrl = PlayerPrefs.GetString(PENDING_LINK_QR_KEY, "");
            _deviceCodeExpiresAt = expiresAt;
            _deviceCodePollInterval = Mathf.Max(1.0f, PlayerPrefs.GetFloat(PENDING_LINK_INTERVAL_KEY, 5.0f));
            // Awake can run before the game sets its ID at runtime (the
            // CheddaBoards.Instance + SetGameId() pattern), so only start
            // polling now if the ID already matches; otherwise SetGameId()
            // resumes or discards the code once it knows the real game.
            if (string.IsNullOrEmpty(savedGame) || savedGame == gameId)
            {
                Log($"Resuming device code link after reload: {RedactCode(_deviceUserCode)} ({(int)(expiresAt - GetUnixTime())}s left)");
                StartDeviceCodePolling();
            }
            else
            {
                Log($"Pending device code restored for game '{savedGame}' - waiting for SetGameId() before polling");
            }
        }

        // Called from SetGameId(): resume or discard a code restored before
        // the game ID was known.
        private void ReconcilePendingLinkWithGameId()
        {
            if (string.IsNullOrEmpty(_deviceCode) || _isPollingDeviceCode)
                return;
            if (!HasPendingDeviceCode())
            {
                ClearDeviceCodeState();
                return;
            }
            if (string.IsNullOrEmpty(_pendingLinkGameId) || _pendingLinkGameId == gameId)
            {
                Log($"Resuming device code link after reload: {RedactCode(_deviceUserCode)} ({GetDeviceCodeSecondsRemaining()}s left)");
                StartDeviceCodePolling();
            }
            else
            {
                Log("Saved device code belongs to another game - discarding");
                ClearDeviceCodeState();
            }
        }

        private void ExpireSession()
        {
            // Server rejected the stored session token - clear everything
            // and tell the game so it can return to its login screen.
            Log("Session expired or rejected by server - clearing");
            _sessionToken = "";
            _authType = "";
            _nickname = "";
            _cachedProfile.Clear();
            ClearSavedSession();
            OnSessionExpired?.Invoke();
            OnLogoutSuccess?.Invoke();
        }

        private string SanitizePlayerId(string rawId)
        {
            if (string.IsNullOrEmpty(rawId))
                return GetPlayerId();

            var sb = new StringBuilder();
            foreach (char c in rawId)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')
                    sb.Append(c);
            }

            string sanitized = sb.ToString();

            if (string.IsNullOrEmpty(sanitized))
                return "p_" + Math.Abs(rawId.GetHashCode()).ToString();

            if (sanitized[0] >= '0' && sanitized[0] <= '9')
                sanitized = "p_" + sanitized;

            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);

            return sanitized;
        }

        // ============================================================
        // PUBLIC API - AUTHENTICATION
        // ============================================================

        /// <summary>Anonymous login - uses API key + persistent device ID.</summary>
        public void LoginAnonymous(string nickname = "")
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                OnLoginFailed?.Invoke("API key not set. Call SetApiKey() first.");
                return;
            }

            // Only store a nickname if one was explicitly provided. Do NOT
            // auto-generate a placeholder — the UI shows "Guest" instead.
            _nickname = !string.IsNullOrEmpty(nickname) ? nickname : "";
            _authType = "anonymous";
            OnLoginSuccess?.Invoke(_nickname);
            Log($"Anonymous login: {_nickname} (player: {GetPlayerId()})");
        }

        /// <summary>Social login (Google, Apple, etc.) via Device Code Auth.</summary>
        public void LoginGoogle()
        {
            Log("Google login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Social login (Google, Apple, etc.) via Device Code Auth.</summary>
        public void LoginApple()
        {
            Log("Apple login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Internet Identity login via Device Code Auth.</summary>
        public void LoginInternetIdentity(string nickname = "")
        {
            Log("II login → use LoginWithDeviceCode() for cross-platform social login");
            LoginWithDeviceCode();
        }

        /// <summary>Alias for LoginInternetIdentity.</summary>
        public void LoginCheddaId(string nickname = "") => LoginInternetIdentity(nickname);

        public void Logout()
        {
            _cachedProfile.Clear();
            _authType = "";
            _nickname = "";
            _sessionToken = "";
            _playSessionToken = "";
            ClearSavedSession();
            CancelDeviceCode();
            OnLogoutSuccess?.Invoke();
            Log("Logged out");
        }

        public bool IsAuthenticated()
        {
            if (_authType == "anonymous") return true;
            return !string.IsNullOrEmpty(_sessionToken);
        }

        public bool IsAnonymous() => _authType == "anonymous" || string.IsNullOrEmpty(_sessionToken);

        public bool HasAccount() => IsAuthenticated() && !IsAnonymous();

        public void RefreshProfile()
        {
            if (_isRefreshingProfile) return;

            float currentTime = Time.time;
            // Allow the first-ever call; the cooldown applies from the second call on.
            if (_lastProfileRefresh > 0f && currentTime - _lastProfileRefresh < PROFILE_REFRESH_COOLDOWN) return;

            _isRefreshingProfile = true;
            _lastProfileRefresh = currentTime;
            GetPlayerProfile();
            Log("Profile refresh requested");
        }

        public void ChangeNickname(string newNickname = "")
        {
            // Canonical rule (matches proxy + canister): 3-16 chars, letters/numbers/underscores.
            if (string.IsNullOrEmpty(newNickname) || newNickname.Length < 3)
            {
                OnNicknameError?.Invoke("Nickname must be at least 3 characters");
                return;
            }
            if (newNickname.Length > 16)
            {
                OnNicknameError?.Invoke("Nickname must be 16 characters or less");
                return;
            }
            foreach (char c in newNickname)
            {
                bool valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                             (c >= '0' && c <= '9') || c == '_';
                if (!valid)
                {
                    OnNicknameError?.Invoke("Nickname can only contain letters, numbers, and underscores");
                    return;
                }
            }

            // Anonymous players who have never touched the backend don't exist
            // there yet, so a rename has nowhere to land - stash it locally.
            // It rides the first submit (SubmitScore sends _nickname when set)
            // and is re-synced from the profile path as a belt-and-braces.
            // Gate on confirmed existence, never on _cachedProfile (see field docs).
            if (IsAnonymous() && !_playerExistsOnBackend)
            {
                _nickname = newNickname;
                _pendingServerNickname = newNickname;
                _pendingRenameAttempts = 0;
                Log($"Nickname set locally (player not on backend yet): {newNickname} - will sync on first submit");
                OnNicknameChanged?.Invoke(newNickname);
                return;
            }

            if (!string.IsNullOrEmpty(_sessionToken))
            {
                _requestedNickname = newNickname;
                var body = new Dictionary<string, object> { { "nickname", newNickname } };
                MakeHttpRequest("/profile/nickname", "PUT", body, "change_nickname");
                Log($"Nickname change requested (session) -> {newNickname}");
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                string pid = GetPlayerId();
                if (string.IsNullOrEmpty(pid))
                {
                    OnNicknameError?.Invoke("No player ID set");
                    return;
                }
                _requestedNickname = newNickname;
                var body = new Dictionary<string, object> { { "nickname", newNickname } };
                string url = $"/players/{Uri.EscapeDataString(pid)}/nickname";
                MakeHttpRequest(url, "PUT", body, "change_nickname_anonymous");
                Log($"Nickname change requested (API) for: {pid} -> {newNickname}");
            }
            else
            {
                OnNicknameError?.Invoke("Not authenticated");
            }
        }

        // ============================================================
        // PUBLIC API - DEVICE CODE AUTH (Cross-platform social login)
        // ============================================================

        /// <summary>Start device code login flow.
        /// Emits OnDeviceCodeReceived with the code to show the player.
        /// Automatically polls for approval and emits OnDeviceCodeApproved on success.
        ///
        /// If an unexpired code is already pending (in progress, or restored from
        /// PlayerPrefs after an app restart / page reload), that code is
        /// re-emitted via OnDeviceCodeReceived and polling continues on it - no
        /// new code is minted, so an approval the player already gave still
        /// lands. Pass forceNew = true to discard the pending code and request a
        /// fresh one.</summary>
        public void LoginWithDeviceCode(bool forceNew = false)
        {
            if (!_initComplete)
            {
                OnDeviceCodeError?.Invoke("CheddaBoards not ready");
                return;
            }

            if (string.IsNullOrEmpty(gameId))
            {
                OnDeviceCodeError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }

            if (!forceNew && HasPendingDeviceCode())
            {
                Log($"Reusing pending device code: {RedactCode(_deviceUserCode)}");
                if (!_isPollingDeviceCode)
                    StartDeviceCodePolling();
                OnDeviceCodeReceived?.Invoke(_deviceUserCode, _deviceVerificationUrl, _deviceQrDataUrl);
                // Someone may have approved it while we were reloading - check now.
                if (!_deviceCodePollInFlight)
                {
                    _deviceCodePollInFlight = true;
                    StartCoroutine(PollDeviceCodeToken());
                }
                return;
            }

            StopDeviceCodePolling();
            ClearDeviceCodeState();

            Log($"Requesting device code for game: {gameId}");
            var body = new Dictionary<string, object> { { "gameId", gameId } };
            // Seed the player's current nickname so that if this link CREATES a new
            // account, it's born with the name they chose in-game (server suffixes
            // on collision). Existing accounts are unaffected - the canister ignores
            // the nickname for known users. Fixes new accounts landing as "Player_N".
            if (!string.IsNullOrEmpty(_nickname))
                body["nickname"] = _nickname;
            MakeHttpRequest("/auth/device/code", "POST", body, "device_code_request");
        }

        /// <summary>Abandon an in-progress device code login. Stops polling, forgets
        /// the code and deletes the saved pending code, so an approval the player
        /// gives AFTER this call is never picked up (the link page will still say
        /// "success" - it can't know the game gave up).
        ///
        /// Call this only when the player explicitly abandons the login ("Cancel",
        /// "Use a different account"). Do NOT call it when they simply close the
        /// code/QR popup: hide the popup and leave polling running, and
        /// OnDeviceCodeApproved / OnLoginSuccess will still fire when their phone
        /// finishes. Polling stops by itself on approval or expiry.</summary>
        public void CancelDeviceCode()
        {
            bool hadCode = !string.IsNullOrEmpty(_deviceCode);
            StopDeviceCodePolling();
            ClearDeviceCodeState();
            if (hadCode)
                Log("Device code login cancelled");
        }

        /// <summary>Check whether an unexpired device code is waiting for approval.
        /// True both for a code requested this session and for one restored from
        /// PlayerPrefs after a reload. Unlike IsDeviceCodePending() this does not
        /// require polling to be active.</summary>
        public bool HasPendingDeviceCode()
        {
            if (string.IsNullOrEmpty(_deviceCode))
                return false;
            return GetUnixTime() < _deviceCodeExpiresAt;
        }

        /// <summary>Drop all in-memory device code state and the saved pending code.</summary>
        private void ClearDeviceCodeState()
        {
            _deviceCode = "";
            _deviceUserCode = "";
            _deviceVerificationUrl = "";
            _deviceQrDataUrl = "";
            _deviceCodeExpiresAt = 0;
            _pendingLinkGameId = "";
            ClearPendingLink();
        }

        /// <summary>Get the current user code (for display purposes).</summary>
        public string GetDeviceUserCode() => _deviceUserCode;

        /// <summary>Link URL for the current code (empty if none). Lets a popup
        /// re-show a restored code without waiting for a new OnDeviceCodeReceived.</summary>
        public string GetDeviceVerificationUrl() => _deviceVerificationUrl;

        /// <summary>Seconds until the current device code expires (0 if none). A
        /// code restored after a reload has LESS than the original 300s left, so
        /// UIs should read this rather than assume a fresh 5 minutes.</summary>
        public int GetDeviceCodeSecondsRemaining()
        {
            if (string.IsNullOrEmpty(_deviceCode))
                return 0;
            return Math.Max(0, (int)(_deviceCodeExpiresAt - GetUnixTime()));
        }

        /// <summary>Check if a device code login is in progress.</summary>
        public bool IsDeviceCodePending() => _isPollingDeviceCode && !string.IsNullOrEmpty(_deviceCode);

        // ============================================================
        // DEVICE CODE POLLING (Internal)
        // ============================================================

        private void StartDeviceCodePolling()
        {
            StopDeviceCodePolling();
            _isPollingDeviceCode = true;
            _deviceCodePollInFlight = false;
            _deviceCodeApprovedFlag = false;

            _deviceCodePollCoroutine = StartCoroutine(DeviceCodePollLoop());
            Log($"Device code polling started (every {(int)_deviceCodePollInterval}s)");
        }

        private void StopDeviceCodePolling()
        {
            _isPollingDeviceCode = false;
            _deviceCodePollInFlight = false;
            if (_deviceCodePollCoroutine != null)
            {
                StopCoroutine(_deviceCodePollCoroutine);
                _deviceCodePollCoroutine = null;
            }
        }

        private IEnumerator DeviceCodePollLoop()
        {
            while (_isPollingDeviceCode && !string.IsNullOrEmpty(_deviceCode))
            {
                yield return new WaitForSecondsRealtime(_deviceCodePollInterval);

                if (!_isPollingDeviceCode || string.IsNullOrEmpty(_deviceCode))
                    yield break;

                if (_deviceCodePollInFlight) continue;

                // Check expiry
                if (GetUnixTime() >= _deviceCodeExpiresAt)
                {
                    Log($"Device code expired: {RedactCode(_deviceUserCode)}");
                    StopDeviceCodePolling();
                    ClearDeviceCodeState();
                    OnDeviceCodeExpired?.Invoke();
                    yield break;
                }

                _deviceCodePollInFlight = true;
                yield return StartCoroutine(PollDeviceCodeToken());
            }
        }

        private IEnumerator PollDeviceCodeToken()
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            };
            if (!string.IsNullOrEmpty(gameId))
                headers["X-Game-ID"] = gameId;

            string body = DictToJson(new Dictionary<string, object> { { "device_code", _deviceCode } });
            string url = API_BASE_URL + "/auth/device/token";

            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            foreach (var h in headers)
                request.SetRequestHeader(h.Key, h.Value);

            yield return request.SendWebRequest();

            _deviceCodePollInFlight = false;
            long responseCode = request.responseCode;
            string responseText = request.downloadHandler?.text ?? "";
            bool networkError = request.result == UnityWebRequest.Result.ConnectionError;
            request.Dispose();

            if (networkError)
            {
                Log("Device code poll: network error");
                yield break;
            }

            if (_deviceCodeApprovedFlag)
            {
                Log("Device code poll: ignoring response (already approved)");
                yield break;
            }

            var response = ParseJson(responseText) as Dictionary<string, object>;
            if (response == null)
            {
                Log("Device code poll: invalid JSON");
                yield break;
            }

            // 428 = authorization_pending (keep polling)
            if (responseCode == 428)
                yield break;

            // 410 = expired
            if (responseCode == 410)
            {
                Log("Device code expired (server confirmed)");
                StopDeviceCodePolling();
                ClearDeviceCodeState();
                OnDeviceCodeExpired?.Invoke();
                yield break;
            }

            // 200 = approved!
            if (responseCode == 200)
            {
                bool ok = response.ContainsKey("ok") && response["ok"] is bool b && b;
                if (ok)
                {
                    _deviceCodeApprovedFlag = true;
                    StopDeviceCodePolling();

                    var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : new Dictionary<string, object>();
                    if (data == null) data = new Dictionary<string, object>();

                    string sessionId = GetString(data, "sessionId", "");
                    string nickname = GetString(data, "nickname", "Player");
                    string email = GetString(data, "email", "");

                    Log($"Device code approved! User: {nickname} ({RedactEmail(email)})");

                    // Save anonymous player ID BEFORE switching auth — needed for migration
                    string previousAnonymousId = _playerId;
                    bool wasAnonymous = _authType == "anonymous" && !string.IsNullOrEmpty(previousAnonymousId);

                    // Fresh-account nickname preservation: if this link CREATED the account
                    // (isNewUser) and the player was anonymous with a chosen name, restore
                    // that name after migration instead of keeping the generated one.
                    // Existing accounts keep their own nickname (merge case) - untouched.
                    bool isNewUser = data.ContainsKey("isNewUser") && data["isNewUser"] is bool nu && nu;
                    _pendingNicknameRestore = "";
                    if (wasAnonymous && isNewUser && !string.IsNullOrEmpty(_nickname) && _nickname != nickname)
                    {
                        _pendingNicknameRestore = _nickname;
                        Log($"New account created at link time - will restore anon nickname '{_nickname}' after migration");
                    }

                    // Set session state
                    _sessionToken = sessionId;
                    _nickname = nickname;
                    _authType = GetString(data, "provider", "google"); // Real provider from proxy; older proxies omit it
                    SaveSession();

                    // Clear stale anonymous play session
                    if (!string.IsNullOrEmpty(_playSessionToken))
                    {
                        Log("Clearing stale anonymous play session after device code auth");
                        _playSessionToken = "";
                    }

                    // Cache profile data
                    var gameProfile = data.ContainsKey("gameProfile") ? data["gameProfile"] as Dictionary<string, object> : null;
                    if (gameProfile != null)
                    {
                        UpdateCachedProfile(new Dictionary<string, object>
                        {
                            { "nickname", nickname },
                            { "gameProfile", gameProfile }
                        });
                    }
                    else
                    {
                        _cachedProfile = new Dictionary<string, object> { { "nickname", nickname } };
                        OnProfileLoaded?.Invoke(nickname, 0, 0, new List<object>(), 0);
                    }

                    // Clear device code state (memory + saved pending code)
                    ClearDeviceCodeState();

                    // Emit both signals so existing login flows work
                    OnDeviceCodeApproved?.Invoke(nickname);
                    OnLoginSuccess?.Invoke(nickname);

                    // Auto-migrate anonymous data → new account
                    if (wasAnonymous)
                        MigrateAnonymousAccount(previousAnonymousId);

                    yield break;
                }
            }

            // 404 = invalid code (or already consumed)
            if (responseCode == 404)
            {
                if (_deviceCodeApprovedFlag || string.IsNullOrEmpty(_deviceCode))
                {
                    Log("Device code poll: 404 after approval (ignoring)");
                    yield break;
                }
                Log("Device code invalid or expired");
                StopDeviceCodePolling();
                ClearDeviceCodeState();
                OnDeviceCodeError?.Invoke("Invalid or expired code");
                yield break;
            }

            // Other errors - log but keep polling
            string errorMsg = GetString(response, "error", "Unknown error");
            Log($"Device code poll error ({responseCode}): {errorMsg}");
        }

        // ============================================================
        // ACCOUNT MIGRATION (Anonymous → Verified)
        // ============================================================

        private void MigrateAnonymousAccount(string anonymousDeviceId)
        {
            if (string.IsNullOrEmpty(anonymousDeviceId) || string.IsNullOrEmpty(_sessionToken))
            {
                Log("Migration skipped: missing device ID or session token");
                OnAccountUpgradeFailed?.Invoke("Missing device ID or session token");
                return;
            }

            Log($"Migrating anonymous data: {anonymousDeviceId} → authenticated account");
            var body = new Dictionary<string, object> { { "deviceId", anonymousDeviceId } };
            MakeHttpRequest("/migrate-account", "POST", body, "migrate_account");
        }

        /// <summary>Public method: manually trigger migration if needed.</summary>
        public void MigrateAnonymousToCurrent(string anonymousDeviceId)
        {
            MigrateAnonymousAccount(anonymousDeviceId);
        }

        // ============================================================
        // PUBLIC API - SCORES
        // ============================================================

        public void SubmitScore(int score, int streak = 0)
        {
            if (!IsAuthenticated())
            {
                Log("Not authenticated, cannot submit");
                OnScoreError?.Invoke("Not authenticated");
                return;
            }

            if (_isSubmittingScore)
            {
                Log("Score submission already in progress");
                return;
            }

            _isSubmittingScore = true;
            _pendingScore = score;
            _pendingStreak = streak;

            // Only send a nickname the caller actually set. Omitting the field
            // lets the server keep the existing profile name — a submit should
            // update the score, not silently rename the player.
            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "gameId", gameId },
                { "score", score },
                { "streak", streak }
            };
            if (!string.IsNullOrEmpty(_nickname))
                body["nickname"] = _nickname;
            if (!string.IsNullOrEmpty(_playSessionToken))
                body["playSessionToken"] = _playSessionToken;

            Log($"Submitting: score={score}, streak={streak}, nickname={(string.IsNullOrEmpty(_nickname) ? "(unset)" : _nickname)}, gameId={gameId}, playerId={body["playerId"]}, session={(_playSessionToken.Length > 20 ? _playSessionToken.Substring(0, 20) : _playSessionToken)}");
            MakeHttpRequest("/scores", "POST", body, "submit_score");
        }

        public void SubmitScoreWithAchievements(int score, int streak, List<string> achievements)
        {
            if (!IsAuthenticated())
            {
                Log("Not authenticated, cannot submit");
                OnScoreError?.Invoke("Not authenticated");
                return;
            }
            if (_isSubmittingScore)
            {
                Log("Score submission already in progress");
                return;
            }
            _isSubmittingScore = true;
            _pendingScore = score;
            _pendingStreak = streak;

            var achIds = new List<string>();
            foreach (var ach in achievements)
            {
                if (!string.IsNullOrEmpty(ach))
                    achIds.Add(ach);
            }

            Log($"Submitting score with {achIds.Count} achievements (HTTP API)");

            // Store achievement IDs - queued AFTER score succeeds
            _deferredAchievementIds = new List<string>(achIds);
            _deferredAchievementsRemaining = 0;
            _deferredAchievementsSynced = new List<string>();

            // Submit score FIRST (creates/updates player profile on backend).
            // Nickname is only sent when the caller actually set one — see SubmitScore.
            var scoreBody = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "gameId", gameId },
                { "score", score },
                { "streak", streak }
            };
            if (!string.IsNullOrEmpty(_nickname))
                scoreBody["nickname"] = _nickname;
            if (!string.IsNullOrEmpty(_playSessionToken))
                scoreBody["playSessionToken"] = _playSessionToken;

            Log($"Submitting: score={score}, streak={streak}, nickname={(string.IsNullOrEmpty(_nickname) ? "(unset)" : _nickname)}, gameId={gameId}, playerId={scoreBody["playerId"]}, session={(_playSessionToken.Length > 20 ? _playSessionToken.Substring(0, 20) : _playSessionToken)}");
            MakeHttpRequest("/scores", "POST", scoreBody, "submit_score");
        }

        /// <summary>
        /// Submit a score to ONE specific (targeted) scoreboard, by ID.
        ///
        /// Unlike SubmitScore(), this does NOT fan out to your all-time / weekly /
        /// daily boards and does NOT update the player's overall profile total. It
        /// writes to the named board only. The board must exist AND be configured
        /// as a "targeted" board in the dashboard, otherwise the backend returns an
        /// error.
        ///
        /// Use it for per-level, per-mode, or category leaderboards:
        ///     CheddaBoards.Instance.SubmitScoreToBoard("level-14", score, streak);
        ///
        /// If the game has time validation enabled, start a play session first
        /// (StartPlaySession) exactly as you would for SubmitScore — the active
        /// play-session token is attached automatically when present.
        ///
        /// Fires OnScoreSubmittedToBoard(scoreboardId, score, streak) on success,
        /// or OnScoreError(reason) on failure. Safe to call several times in a row
        /// for different boards; each call carries its score / streak / board id in
        /// request meta rather than the shared _pending* fields, so queued submits
        /// don't clobber each other.
        /// </summary>
        public void SubmitScoreToBoard(string scoreboardId, int score, int streak = 0)
        {
            if (!IsAuthenticated())
            {
                Log("Not authenticated, cannot submit to board");
                OnScoreError?.Invoke("Not authenticated");
                return;
            }

            if (string.IsNullOrEmpty(scoreboardId))
            {
                OnScoreError?.Invoke("scoreboardId is required");
                return;
            }

            // Nickname only sent when the caller actually set one — see SubmitScore.
            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "gameId", gameId },
                { "score", score },
                { "streak", streak },
                { "scoreboardId", scoreboardId }
            };
            if (!string.IsNullOrEmpty(_nickname))
                body["nickname"] = _nickname;
            if (!string.IsNullOrEmpty(_playSessionToken))
                body["playSessionToken"] = _playSessionToken;

            Log($"Submitting to board '{scoreboardId}': score={score}, streak={streak}, player={body["playerId"]}");
            MakeHttpRequest("/scores", "POST", body, "submit_score_to_board", new Dictionary<string, object>
            {
                { "scoreboard_id", scoreboardId },
                { "score", score },
                { "streak", streak }
            });
        }

        // ============================================================
        // PUBLIC API - PLAY SESSIONS (Time Validation)
        // ============================================================

        public void StartPlaySession()
        {
            _playSessionToken = "";
            var body = new Dictionary<string, object>
            {
                { "gameId", gameId },
                { "playerId", GetPlayerId() }
            };
            MakeHttpRequest("/play-sessions/start", "POST", body, "start_play_session");
            Log($"Play session requested for game: {gameId}, player: {GetPlayerId()}");
        }

        public string GetPlaySessionToken() => _playSessionToken;
        public bool HasPlaySession() => !string.IsNullOrEmpty(_playSessionToken);

        public void EndPlaySession()
        {
            if (string.IsNullOrEmpty(_playSessionToken) || _playSessionToken.StartsWith("fallback_"))
            {
                Log("No active server session to end");
                _playSessionToken = "";
                return;
            }

            Log($"Ending play session on server: {_playSessionToken.Substring(0, Math.Min(30, _playSessionToken.Length))}");
            var body = new Dictionary<string, object> { { "playSessionToken", _playSessionToken } };
            MakeHttpRequest("/play-sessions/end", "POST", body, "end_play_session");
            _playSessionToken = "";
        }

        public void ClearPlaySession()
        {
            if (!string.IsNullOrEmpty(_playSessionToken) && !_playSessionToken.StartsWith("fallback_"))
                EndPlaySession();
            else
            {
                _playSessionToken = "";
                Log("Play session cleared (local only)");
            }
        }

        // ============================================================
        // PUBLIC API - LEADERBOARDS
        // ============================================================

        public void GetLeaderboard(string sortBy = "score", int limit = 100)
        {
            string url = $"/leaderboard?sort={sortBy}&limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "leaderboard");
            Log($"Leaderboard requested (sort: {sortBy}, limit: {limit})");
        }

        public void GetPlayerRank(string sortBy = "score")
        {
            string url = $"/players/{Uri.EscapeDataString(GetPlayerId())}/rank?sort={sortBy}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "player_rank");
            Log($"Player rank requested (sort: {sortBy})");
        }

        public void GetPlayerProfile(string playerId = "")
        {
            if (!string.IsNullOrEmpty(_sessionToken))
            {
                MakeHttpRequest("/auth/profile", "GET", new Dictionary<string, object>(), "player_profile");
                Log("Player profile requested (session)");
            }
            else
            {
                string pid = !string.IsNullOrEmpty(playerId) ? playerId : GetPlayerId();
                if (string.IsNullOrEmpty(pid))
                {
                    Log("No player ID for profile fetch");
                    OnNoProfile?.Invoke();
                    return;
                }
                string url = $"/players/{Uri.EscapeDataString(pid)}/profile";
                MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "player_profile");
                Log($"Player profile requested for: {pid}");
            }
        }

        // ============================================================
        // PUBLIC API - SCOREBOARDS (Time-based Leaderboards)
        // ============================================================

        public void GetScoreboards(string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_scoreboards");
            Log($"Scoreboards list requested for game: {gid}");
        }

        public void GetScoreboard(string scoreboardId, int limit = 100, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_scoreboard",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Scoreboard '{scoreboardId}' requested (limit: {limit})");
        }

        public void GetScoreboardRank(string scoreboardId, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnScoreboardError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            if (string.IsNullOrEmpty(_sessionToken))
            {
                OnScoreboardError?.Invoke("Session token required for rank lookup");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/rank";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "scoreboard_rank",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Scoreboard rank requested for '{scoreboardId}'");
        }

        public void GetWeeklyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("weekly", limit, forGameId);

        public void GetDailyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("daily", limit, forGameId);

        public void GetAlltimeLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("all-time", limit, forGameId);

        public void GetMonthlyLeaderboard(int limit = 100, string forGameId = "") =>
            GetScoreboard("monthly", limit, forGameId);

        // ============================================================
        // PUBLIC API - SCOREBOARD ARCHIVES
        // ============================================================

        public void GetScoreboardArchives(string scoreboardId, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_archives",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Archives list requested for '{scoreboardId}'");
        }

        public void GetLastArchivedScoreboard(string scoreboardId, int limit = 100, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives/latest?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_last_archive",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Last archive requested for '{scoreboardId}'");
        }

        public void GetArchivedScoreboard(string archiveId, int limit = 100)
        {
            string url = $"/archives/{Uri.EscapeDataString(archiveId)}?limit={limit}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "get_archive",
                new Dictionary<string, object> { { "archive_id", archiveId } });
            Log($"Archive '{archiveId}' requested");
        }

        public void GetArchivesInRange(string scoreboardId, long afterTimestamp, long beforeTimestamp, string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/scoreboards/{Uri.EscapeDataString(scoreboardId)}/archives?after={afterTimestamp}&before={beforeTimestamp}";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "list_archives",
                new Dictionary<string, object> { { "scoreboard_id", scoreboardId } });
            Log($"Archives in range requested for '{scoreboardId}'");
        }

        public void GetArchiveStats(string forGameId = "")
        {
            string gid = !string.IsNullOrEmpty(forGameId) ? forGameId : gameId;
            if (string.IsNullOrEmpty(gid))
            {
                OnArchiveError?.Invoke("Game ID not set. Call SetGameId() first.");
                return;
            }
            string url = $"/games/{Uri.EscapeDataString(gid)}/archives/stats";
            MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "archive_stats");
            Log($"Archive stats requested for game: {gid}");
        }

        public void GetLastWeekScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("weekly", limit, forGameId);

        public void GetLastMonthScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("monthly", limit, forGameId);

        public void GetYesterdayScoreboard(int limit = 100, string forGameId = "") =>
            GetLastArchivedScoreboard("daily", limit, forGameId);

        // ============================================================
        // PUBLIC API - ACHIEVEMENTS
        // ============================================================

        public void UnlockAchievement(string achievementId, string achievementName = "", string achievementDesc = "")
        {
            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementId", achievementId }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement");
            Log($"Achievement unlock (async): {achievementId}");
        }

        /// <summary>Unlock multiple achievements in a single request.</summary>
        public void UnlockAchievementsBatch(List<string> achievementIds)
        {
            if (achievementIds == null || achievementIds.Count == 0) return;

            Log($"Batch unlocking {achievementIds.Count} achievements...");
            _deferredAchievementsRemaining = 1;
            _deferredAchievementsSynced = new List<string>();
            _lastBatchIds = new List<string>(achievementIds);

            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementIds", achievementIds }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement_batch");
        }

        public void GetAchievements(string playerId = "")
        {
            // Achievements live on the profile (gameProfile.achievements) -
            // there is no standalone /players/{id}/achievements route (the
            // old URL returned "Unknown endpoint"). This fetches the profile
            // and surfaces just the achievements via OnAchievementsLoaded;
            // OnProfileLoaded does NOT fire for this call.
            if (!string.IsNullOrEmpty(_sessionToken) && string.IsNullOrEmpty(playerId))
            {
                MakeHttpRequest("/auth/profile", "GET", new Dictionary<string, object>(), "achievements");
                Log("Achievements requested (session profile)");
            }
            else
            {
                string pid = !string.IsNullOrEmpty(playerId) ? playerId : GetPlayerId();
                if (string.IsNullOrEmpty(pid))
                {
                    Log("No player ID for achievements fetch");
                    OnAchievementsLoaded?.Invoke(new List<object>());
                    return;
                }
                string url = $"/players/{Uri.EscapeDataString(pid)}/profile";
                MakeHttpRequest(url, "GET", new Dictionary<string, object>(), "achievements");
                Log($"Achievements requested for: {pid}");
            }
        }

        /// <summary>Send all deferred achievements in a single batch request.</summary>
        private void FlushDeferredAchievements()
        {
            if (_deferredAchievementIds.Count == 0) return;
            int count = _deferredAchievementIds.Count;
            _deferredAchievementsRemaining = 1;
            _deferredAchievementsSynced = new List<string>();
            _lastBatchIds = new List<string>(_deferredAchievementIds);
            Log($"Batch syncing {count} achievements...");

            var body = new Dictionary<string, object>
            {
                { "playerId", GetPlayerId() },
                { "achievementIds", new List<string>(_deferredAchievementIds) }
            };
            MakeHttpRequestAsync("/achievements", "POST", body, "unlock_achievement_batch");
            _deferredAchievementIds.Clear();
        }

        // ============================================================
        // PUBLIC API - ANALYTICS
        // ============================================================

        public void TrackEvent(string eventType, Dictionary<string, object> metadata = null)
        {
            // TODO: POST to analytics endpoint when available
            Log($"Event tracked (local): {eventType} {(metadata != null ? DictToJson(metadata) : "")}");
        }

        // ============================================================
        // PUBLIC API - GAME INFO
        // ============================================================

        public void GetGameInfo() => MakeHttpRequest("/game", "GET", new Dictionary<string, object>(), "game_info");
        /// <summary>Game totals (totalPlayers, totalPlays) are part of GET /game; there
        /// is no separate stats route. Kept for compatibility - identical to
        /// GetGameInfo(). (Before 2.3.0 this requested /game/stats, which doesn't
        /// exist, and failed with OnRequestFailed every time.)</summary>
        public void GetGameStats() => MakeHttpRequest("/game", "GET", new Dictionary<string, object>(), "game_stats");
        public void HealthCheck() => MakeHttpRequest("/health", "GET", new Dictionary<string, object>(), "health");

        // ============================================================
        // DEBUG
        // ============================================================

        public void DebugStatus()
        {
            Debug.Log("");
            Debug.Log("╔══════════════════════════════════════════════╗");
            Debug.Log("║        CheddaBoards Debug Status v2.2.1      ║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Configuration                                ║");
            Debug.Log($"║  - Platform:         {Application.platform.ToString().PadRight(24)}║");
            Debug.Log($"║  - Init Complete:    {_initComplete.ToString().PadRight(24)}║");
            Debug.Log($"║  - Game ID:          {(gameId.Length > 20 ? gameId.Substring(0, 20) : gameId).PadRight(24)}║");
            Debug.Log($"║  - API Key Set:      {(!string.IsNullOrEmpty(apiKey)).ToString().PadRight(24)}║");
            Debug.Log($"║  - Session Token:    {(!string.IsNullOrEmpty(_sessionToken)).ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Authentication                               ║");
            Debug.Log($"║  - Authenticated:    {IsAuthenticated().ToString().PadRight(24)}║");
            Debug.Log($"║  - Auth Type:        {_authType.PadRight(24)}║");
            Debug.Log($"║  - Player ID:        {(GetPlayerId().Length > 20 ? GetPlayerId().Substring(0, 20) : GetPlayerId()).PadRight(24)}║");
            Debug.Log($"║  - Anonymous:        {IsAnonymous().ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ Profile                                      ║");
            Debug.Log($"║  - Nickname:         {GetNickname().PadRight(24)}║");
            Debug.Log($"║  - High Score:       {GetHighScore().ToString().PadRight(24)}║");
            Debug.Log($"║  - Best Streak:      {GetBestStreak().ToString().PadRight(24)}║");
            Debug.Log($"║  - Play Count:       {GetPlayCount().ToString().PadRight(24)}║");
            Debug.Log("╠══════════════════════════════════════════════╣");
            Debug.Log($"║ State                                        ║");
            Debug.Log($"║  - Refreshing:       {_isRefreshingProfile.ToString().PadRight(24)}║");
            Debug.Log($"║  - Submitting:       {_isSubmittingScore.ToString().PadRight(24)}║");
            Debug.Log($"║  - HTTP Busy:        {_httpBusy.ToString().PadRight(24)}║");
            Debug.Log($"║  - Queue Size:       {_requestQueue.Count.ToString().PadRight(24)}║");
            Debug.Log($"║  - Play Session:     {HasPlaySession().ToString().PadRight(24)}║");
            Debug.Log($"║  - Device Code:      {(!string.IsNullOrEmpty(_deviceUserCode) ? RedactCode(_deviceUserCode) : "none").PadRight(24)}║");
            Debug.Log($"║  - DC Polling:       {_isPollingDeviceCode.ToString().PadRight(24)}║");
            Debug.Log("╚══════════════════════════════════════════════╝");
            Debug.Log("");
        }

        // ============================================================
        // CLEANUP
        // ============================================================

        private void OnDestroy()
        {
            StopDeviceCodePolling();
        }

        // When the app regains focus (e.g. the player comes back after completing
        // the link flow on their phone), fire one immediate device-code poll so
        // there's no up-to-interval delay before approval is picked up. Standard
        // RFC 8628 polling allows out-of-schedule polls.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || !_isPollingDeviceCode) return;
            if (_deviceCodePollInFlight || string.IsNullOrEmpty(_deviceCode)) return;
            Log("App regained focus during device code linking — polling immediately");
            _deviceCodePollInFlight = true;
            StartCoroutine(PollDeviceCodeToken());
        }

        // Mobile resume mirrors focus regain.
        private void OnApplicationPause(bool paused)
        {
            if (!paused)
                OnApplicationFocus(true);
        }

        // ============================================================
        // JSON HELPERS (Zero-dependency — no Newtonsoft required)
        // ============================================================

        /// <summary>Simple recursive JSON parser. Returns Dictionary, List, string, double, bool, or null.</summary>
        private static object ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip '{'
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}') { index++; return dict; }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;
                object value = ParseValue(json, ref index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == '}') { index++; break; }
                break;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip '['
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']') { index++; return list; }

            while (index < json.Length)
            {
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == ']') { index++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return "";
            index++; // skip opening quote
            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '\\')
                {
                    index++;
                    if (index < json.Length)
                    {
                        char escaped = json[index];
                        switch (escaped)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (index + 4 < json.Length)
                                {
                                    string hex = json.Substring(index + 1, 4);
                                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                        sb.Append((char)code);
                                    index += 4;
                                }
                                break;
                            default: sb.Append(escaped); break;
                        }
                    }
                }
                else if (c == '"')
                {
                    index++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            if (index < json.Length && json[index] == '-') index++;
            while (index < json.Length && char.IsDigit(json[index])) index++;
            bool isFloat = false;
            if (index < json.Length && json[index] == '.')
            {
                isFloat = true;
                index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
            }
            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
            }
            string numStr = json.Substring(start, index - start);
            if (isFloat)
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
                return 0.0;
            }
            if (long.TryParse(numStr, out long l))
            {
                if (l >= int.MinValue && l <= int.MaxValue) return (int)l;
                return l;
            }
            return 0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true")) { index += 4; return true; }
            if (json.Substring(index).StartsWith("false")) { index += 5; return false; }
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null")) { index += 4; }
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        /// <summary>Serialize a Dictionary to JSON string.</summary>
        private static string DictToJson(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJsonString(kvp.Key)}\":");
                sb.Append(ValueToJson(kvp.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string ValueToJson(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            if (value is int i) return i.ToString();
            if (value is long l) return l.ToString();
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is string s) return $"\"{EscapeJsonString(s)}\"";
            if (value is Dictionary<string, object> dict) return DictToJson(dict);
            if (value is List<string> strList)
            {
                var sb = new StringBuilder("[");
                for (int idx = 0; idx < strList.Count; idx++)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append($"\"{EscapeJsonString(strList[idx])}\"");
                }
                sb.Append("]");
                return sb.ToString();
            }
            if (value is List<object> objList)
            {
                var sb = new StringBuilder("[");
                for (int idx = 0; idx < objList.Count; idx++)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append(ValueToJson(objList[idx]));
                }
                sb.Append("]");
                return sb.ToString();
            }
            return $"\"{EscapeJsonString(value.ToString())}\"";
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ============================================================
        // UTILITY HELPERS
        // ============================================================

        private static string GetString(Dictionary<string, object> dict, string key, string defaultVal = "")
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
                return dict[key].ToString();
            return defaultVal;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] is List<object> list)
                return list;
            return new List<object>();
        }

        // Tolerant batch-response reader. Canonical shape is data.results[] of
        // {success, achievementId}, but this also accepts the shapes a backend
        // plausibly returns: plain id-string arrays, alternate array keys
        // (unlocked / syncedIds / achievements), alternate id keys
        // (id / achievement), and non-bool success flags. Returns empty when
        // nothing matches — callers fall back to the requested ids on HTTP 200.
        private static List<string> ParseBatchSyncedIds(Dictionary<string, object> data)
        {
            var ids = new List<string>();
            if (data == null) return ids;

            foreach (string key in new[] { "results", "unlocked", "syncedIds", "achievements" })
            {
                var list = GetList(data, key);
                if (list.Count == 0) continue;
                foreach (var entry in list)
                {
                    if (entry is string s)
                    {
                        if (!string.IsNullOrEmpty(s)) ids.Add(s);
                        continue;
                    }
                    var d = entry as Dictionary<string, object>;
                    if (d == null) continue;
                    // Success flag is optional; when present, accept any truthy form.
                    if (d.ContainsKey("success") && !Truthy(d["success"])) continue;
                    if (d.ContainsKey("ok") && !Truthy(d["ok"])) continue;
                    string id = GetString(d, "achievementId",
                                GetString(d, "id",
                                GetString(d, "achievement", "")));
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
                if (ids.Count > 0) return ids;
            }
            return ids;
        }

        private static bool Truthy(object v)
        {
            if (v is bool b) return b;
            if (v is string s)
            {
                string t = s.Trim().ToLowerInvariant();
                return t == "true" || t == "1" || t == "ok" || t == "success";
            }
            if (v is int i) return i != 0;
            if (v is long l) return l != 0;
            if (v is float f) return f != 0f;
            if (v is double d2) return d2 != 0d;
            return v != null;
        }

        private string GetMetaString(string key)
        {
            return GetString(_currentMeta, key, "");
        }

        private static double GetUnixTime()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}