using System;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;

namespace DeathTimer.Patches;

// The host owns the timer and kills players when it runs out. Killing goes through
// the game's own PlayerDeathRPC (the master client is allowed to call it on anyone),
// so players without this mod die too. The timer state is shared through Photon
// room properties so that players who have the mod can show it on their HUD.
[HarmonyPatch]
public static class TimerPatches
{
    private enum TimerState
    {
        Idle = 0,
        Running = 1,
        Stopped = 2,
        Expired = 3
    }

    private const string KeyState = "maxenterme.DeathTimer.state";
    private const string KeyEnd = "maxenterme.DeathTimer.end";
    private const string KeyRemaining = "maxenterme.DeathTimer.remaining";

    // Seconds between kill sweeps after time is up (catches players revived later).
    private const float KillSweepInterval = 1f;

    // Host state
    private static TimerState _state = TimerState.Idle;
    private static double _startTime;
    private static double _endTime;
    private static float _remainingAtStop;
    private static float _nextKillSweep;

    // HUD
    private static GameObject? _hudObject;
    private static TextMeshProUGUI? _hudText;
    private static TMP_FontAsset? _cachedFont;
    private static float _lastFontSize;
    private static float _lastPosX;
    private static float _lastPosY;

    [HarmonyPatch(typeof(RoundDirector), "Update")]
    [HarmonyPostfix]
    private static void RoundDirector_Update_Postfix()
    {
        try
        {
            if (!SemiFunc.RunIsLevel()) return;

            if (SemiFunc.IsMasterClientOrSingleplayer())
                UpdateHost();

            if (Plugin.HudEnabled.Value)
                UpdateHud();
            else if (_hudObject != null)
                _hudObject.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"DeathTimer error: {e}");
        }
    }

    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.OnSceneSwitch))]
    [HarmonyPrefix]
    private static void OnSceneSwitch_Prefix()
    {
        try
        {
            if (_state != TimerState.Idle && SemiFunc.IsMasterClientOrSingleplayer())
            {
                _state = TimerState.Idle;
                Publish();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"DeathTimer reset error: {e}");
        }
        _state = TimerState.Idle;

        if (_hudObject != null)
        {
            UnityEngine.Object.Destroy(_hudObject);
            _hudObject = null;
            _hudText = null;
        }
        _cachedFont = null;
    }

    private static double Now() =>
        SemiFunc.IsMultiplayer() ? PhotonNetwork.Time : Time.timeAsDouble;

    private static bool InMainGameplay() =>
        GameDirector.instance != null && GameDirector.instance.currentState == GameDirector.gameState.Main;

    private static void UpdateHost()
    {
        if (!Plugin.Enabled.Value)
        {
            // Turned off mid-level (e.g. from an in-game config menu): clear the
            // timer so nobody sees a countdown that will never kill.
            if (_state != TimerState.Idle)
            {
                _state = TimerState.Idle;
                Publish();
            }
            return;
        }

        switch (_state)
        {
            case TimerState.Idle:
                if (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated) return;
                if (!InMainGameplay()) return;
                _startTime = Now();
                _endTime = _startTime + Plugin.TimeLimitSeconds.Value;
                _state = TimerState.Running;
                Publish();
                Plugin.Logger.LogInfo($"Timer started: {Plugin.TimeLimitSeconds.Value}s");
                break;

            case TimerState.Running:
                // Follow a time limit changed mid-level (e.g. from an in-game config menu).
                double endTime = _startTime + Plugin.TimeLimitSeconds.Value;
                if (endTime != _endTime)
                {
                    _endTime = endTime;
                    Publish();
                }

                if (Plugin.StopWhenAllExtracted.Value && RoundDirector.instance.allExtractionPointsCompleted)
                {
                    _remainingAtStop = Mathf.Max(0f, (float)(_endTime - Now()));
                    _state = TimerState.Stopped;
                    Publish();
                    Plugin.Logger.LogInfo($"Timer stopped with {_remainingAtStop:F1}s left");
                }
                else if (Now() >= _endTime)
                {
                    _state = TimerState.Expired;
                    Publish();
                    Plugin.Logger.LogInfo("Time is up: killing all players");
                    _nextKillSweep = 0f;
                }
                break;
        }

        if (_state == TimerState.Expired && Time.time >= _nextKillSweep)
        {
            _nextKillSweep = Time.time + KillSweepInterval;
            KillAllPlayers();
        }
    }

    private static void KillAllPlayers()
    {
        if (!InMainGameplay()) return;
        foreach (var avatar in SemiFunc.PlayerGetAll())
        {
            if (avatar == null || avatar.deadSet || avatar.isDisabled) continue;
            avatar.PlayerDeath(-1);
        }
    }

    private static void Publish()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.InRoom) return;
        var props = new Hashtable
        {
            [KeyState] = (int)_state,
            [KeyEnd] = _endTime,
            [KeyRemaining] = _remainingAtStop
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Reads the timer as everyone sees it: the host's own fields, or the room
    // properties the host published.
    private static bool TryGetDisplay(out TimerState state, out float remaining)
    {
        state = TimerState.Idle;
        remaining = 0f;
        double endTime;

        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            state = _state;
            endTime = _endTime;
            remaining = _remainingAtStop;
        }
        else
        {
            if (!PhotonNetwork.InRoom) return false;
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            if (!props.TryGetValue(KeyState, out object s) || s is not int si) return false;
            state = (TimerState)si;
            endTime = props.TryGetValue(KeyEnd, out object e) && e is double ed ? ed : 0d;
            remaining = props.TryGetValue(KeyRemaining, out object r) && r is float rf ? rf : 0f;
        }

        if (state == TimerState.Running)
            remaining = Mathf.Max(0f, (float)(endTime - Now()));
        else if (state == TimerState.Expired)
            remaining = 0f;

        return state != TimerState.Idle;
    }

    private static void UpdateHud()
    {
        bool hasTimer = TryGetDisplay(out var state, out float remaining);

        if (_hudObject == null)
        {
            if (!hasTimer) return;
            CreateHud();
            if (_hudObject == null || _hudText == null) return;
        }

        _hudObject.SetActive(hasTimer);
        if (!hasTimer || _hudText == null) return;

        ApplyConfigChanges();

        int total = Mathf.CeilToInt(remaining);
        string time = $"{total / 60:D2}:{total % 60:D2}";
        _hudText.text = state switch
        {
            TimerState.Expired => "<color=#ff3030><b>TIME UP</b></color>",
            TimerState.Stopped => $"<color=#55ff55><b>{time}</b></color>",
            _ when total <= Plugin.WarningSeconds.Value => $"<color=#ff3030><b>{time}</b></color>",
            _ => $"<color=#ffffff><b>{time}</b></color>"
        };
    }

    private static void CreateHud()
    {
        if (HUD.instance == null || HUD.instance.hideParent == null) return;

        if (_cachedFont == null)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t.font != null)
                {
                    _cachedFont = t.font;
                    break;
                }
            }
            if (_cachedFont == null) return;
        }

        _hudObject = new GameObject("DeathTimer HUD");
        _hudObject.SetActive(false);
        _hudObject.transform.SetParent(HUD.instance.hideParent.transform, false);

        _hudText = _hudObject.AddComponent<TextMeshProUGUI>();
        _hudText.font = _cachedFont;
        _hudText.alignment = TextAlignmentOptions.Top;
        _hudText.enableWordWrapping = false;
        _hudText.raycastTarget = false;

        var rect = _hudObject.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 60f);

        _lastFontSize = -1f;
        ApplyConfigChanges();
    }

    private static void ApplyConfigChanges()
    {
        if (_hudObject == null || _hudText == null) return;
        float fontSize = Plugin.HudFontSize.Value;
        float posX = Plugin.HudPositionX.Value;
        float posY = Plugin.HudPositionY.Value;
        if (fontSize == _lastFontSize && posX == _lastPosX && posY == _lastPosY) return;

        _lastFontSize = fontSize;
        _lastPosX = posX;
        _lastPosY = posY;
        _hudText.fontSize = fontSize;

        // hideParent does not span the screen, so anchor against the top edge of
        // the root canvas instead, converted into hideParent's local space.
        var rect = _hudObject.GetComponent<RectTransform>();
        Vector3 topCenter = GetScreenTopCenter(rect.parent);
        rect.localPosition = new Vector3(topCenter.x + posX, topCenter.y + posY, 0f);
        Plugin.Logger.LogDebug($"HUD placed at {rect.localPosition} (screen top center {topCenter})");
    }

    private static Vector3 GetScreenTopCenter(Transform parent)
    {
        var canvas = parent.GetComponentInParent<Canvas>();
        if (canvas == null) return Vector3.zero;
        var rootRect = canvas.rootCanvas.GetComponent<RectTransform>();
        var corners = new Vector3[4];
        rootRect.GetWorldCorners(corners);
        // corners: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        Vector3 world = (corners[1] + corners[2]) * 0.5f;
        return parent.InverseTransformPoint(world);
    }
}
