using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DamageMeter;

/// <summary>One player's numbers, as shown in the overlay.</summary>
internal sealed class Row
{
    public ulong Id;
    public Color Color = Color.white;
    public double Total;
    public double Dps;
    public readonly List<KeyValuePair<int, double>> Sources = new(); // spell (>= 0) or Sources.* (< 0), largest first
}

internal static class Sources
{
    internal const int InfusedTornado = -1, Explosions = -2, Other = -3;
}

/// <summary>
/// Counts damage where the game counts it. Enemy health is removed on the host
/// (EnemyHealthContainer.RemoveHealthOnOwner), which then calls RecordDamageDealtToEnemy(amount, damageInfo); that's
/// what feeds the game's own end-of-run damage stats. A postfix there adds the hit to the dealing player and spell.
///
/// The host keeps the totals and sends a small snapshot to clients about once a second (Netcode named message),
/// so everyone sees the same numbers. Clients don't count anything themselves.
/// </summary>
[HarmonyPatch]
internal static class Tracker
{
    private sealed class Acc
    {
        public double Total;
        public readonly float[] Buckets = new float[64];
        public readonly int[] BucketSecond = new int[64];
        public readonly Dictionary<int, double> BySource = new();
        public Color Color = Color.white;
        public bool HasColor;
    }

    private const string MessageName = "girthy_mods.DamageMeter.Snapshot";

    private static readonly Dictionary<ulong, Acc> acc = new();
    private static float runStart = -1f;
    private static float nextSend;

    // What the overlay shows (host: built from acc; client: received).
    internal static List<Row> Rows = new();
    internal static float RunSeconds;
    internal static bool HaveData;
    internal static bool InRun;
    internal static float RunStartedUnscaled;

    private static CustomMessagingManager.HandleNamedMessageDelegate handler; // kept alive for IL2CPP

    // ---------- run start / end ----------

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnNetworkSpawn))]
    [HarmonyPostfix]
    private static void RunSpawned()
    {
        acc.Clear();
        Rows = new List<Row>();
        HaveData = false;
        InRun = true;
        runStart = Time.time;
        RunSeconds = 0f;
        RunStartedUnscaled = Time.unscaledTime;
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && !nm.IsServer && nm.CustomMessagingManager != null)
            {
                handler ??= DelegateSupport.ConvertDelegate<CustomMessagingManager.HandleNamedMessageDelegate>(new Action<ulong, FastBufferReader>(OnSnapshot));
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, handler);
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"Listen: {ex}"); }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnDestroy))]
    [HarmonyPostfix]
    private static void RunDestroyed() { InRun = false; run = null; } // keep the last numbers for the end screen

    // Host: a new run starts (the RunManager can outlive a run, so also clear here).
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRunOnServerRpc))]
    [HarmonyPrefix]
    private static void StartRun_Prefix(RunManager __instance)
    {
        try
        {
            if (__instance.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Execute) return;
            Reset();
            InRun = true;
            RunStartedUnscaled = Time.unscaledTime;
        }
        catch { }
    }

    private static RunManager run;

    /// <summary>A run is being played (not the lobby): the run has started, or damage has been counted.</summary>
    internal static bool Playing
    {
        get
        {
            if (!InRun) return false;
            if (HaveData) return true;
            try
            {
                if (run is null || run.WasCollected || run == null) run = UnityEngine.Object.FindObjectOfType<RunManager>();
                return run != null && (run.runHasStarted || run.currentRunTimeInSeconds > 0f);
            }
            catch { return false; }
        }
    }

    internal static float GameRunSeconds
    {
        get
        {
            try { if (run is not null && !run.WasCollected && run != null) return run.currentRunTimeInSeconds; } catch { }
            return 0f;
        }
    }

    // ---------- counting (host) ----------

    private enum Kind : byte { None, Player, Spell, Explosion, Tornado }

    private static readonly Dictionary<IntPtr, Kind> kinds = new();
    private static int offClientId = -1, offSpell = -1, offOwner = -1, offTornado = -1;
    private static float nextErrorLog;

    [HarmonyPatch(typeof(EnemyHealthContainer), nameof(EnemyHealthContainer.RecordDamageDealtToEnemy))]
    [HarmonyPostfix]
    private static unsafe void Record_Postfix(float amount, DamageInfo damageSource)
    {
        try
        {
            if (!Plugin.Enabled.Value || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount) || damageSource == null) return;
            IntPtr obj = damageSource.Pointer;
            if (obj == IntPtr.Zero) return;
            IntPtr klass = IL2CPP.il2cpp_object_get_class(obj);
            if (!kinds.TryGetValue(klass, out var kind)) { kind = Classify(klass); kinds[klass] = kind; }
            if (kind == Kind.None) return;

            byte* p = (byte*)obj;
            ulong client;
            int source;
            switch (kind)
            {
                case Kind.Spell: client = *(ulong*)(p + offClientId); source = *(int*)(p + offSpell); break;
                case Kind.Player: client = *(ulong*)(p + offClientId); source = Sources.Other; break;
                case Kind.Explosion: client = *(ulong*)(p + offOwner); source = Sources.Explosions; break;
                case Kind.Tornado: client = *(ulong*)(p + offTornado); source = Sources.InfusedTornado; break;
                default: return;
            }

            if (!acc.TryGetValue(client, out var a)) { a = new Acc(); acc[client] = a; }
            a.Total += amount;
            a.BySource.TryGetValue(source, out double s);
            a.BySource[source] = s + amount;

            int sec = Mathf.FloorToInt(Time.time);
            int i = sec & 63;
            if (a.BucketSecond[i] != sec) { a.BucketSecond[i] = sec; a.Buckets[i] = 0f; }
            a.Buckets[i] += amount;

        }
        catch (Exception ex)
        {
            if (Time.unscaledTime < nextErrorLog) return;
            nextErrorLog = Time.unscaledTime + 10f;
            Plugin.Log.LogError($"Counting damage: {ex}");
        }
    }

    private static Kind Classify(IntPtr klass)
    {
        try
        {
            if (Is(klass, Il2CppClassPointerStore<PlayerSpellDamageInfo>.NativeClassPtr))
            {
                offClientId = offClientId >= 0 ? offClientId : Offset(klass, "<OwnerClientId>k__BackingField");
                offSpell = offSpell >= 0 ? offSpell : Offset(klass, "<Spell>k__BackingField");
                return offClientId > 0 && offSpell > 0 ? Kind.Spell : Kind.None;
            }
            if (Is(klass, Il2CppClassPointerStore<PlayerDamageInfo>.NativeClassPtr))
            {
                offClientId = offClientId >= 0 ? offClientId : Offset(klass, "<OwnerClientId>k__BackingField");
                return offClientId > 0 ? Kind.Player : Kind.None;
            }
            if (Is(klass, Il2CppClassPointerStore<PlayerExplosionDamageInfo>.NativeClassPtr))
            {
                offOwner = offOwner >= 0 ? offOwner : Offset(klass, "<OwnerClientId>k__BackingField");
                return offOwner > 0 ? Kind.Explosion : Kind.None;
            }
            if (Is(klass, Il2CppClassPointerStore<InfusedTornadoDamageInfo>.NativeClassPtr))
            {
                offTornado = offTornado >= 0 ? offTornado : Offset(klass, "InfusedByPlayerId");
                return offTornado > 0 ? Kind.Tornado : Kind.None;
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"Damage type check: {ex}"); }
        return Kind.None;
    }

    private static bool Is(IntPtr klass, IntPtr target) =>
        target != IntPtr.Zero && (klass == target || IL2CPP.il2cpp_class_is_subclass_of(klass, target, false));

    // Field offset from the start of the object (searching base classes).
    private static int Offset(IntPtr klass, string field)
    {
        for (IntPtr k = klass; k != IntPtr.Zero; k = IL2CPP.il2cpp_class_get_parent(k))
        {
            IntPtr f = IL2CPP.il2cpp_class_get_field_from_name(k, field);
            if (f != IntPtr.Zero) return (int)IL2CPP.il2cpp_field_get_offset(f);
        }
        Plugin.Log.LogWarning($"Field {field} not found; that kind of damage won't be counted.");
        return -1;
    }

    // ---------- snapshots ----------

    /// <summary>Host / single player: rebuild the rows and, about once a second, send them to clients.</summary>
    internal static void HostTick()
    {
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsListening && nm.IsServer;
        if (!server || !InRun) return;

        float now = Time.time;
        RunSeconds = Mathf.Max(0f, now - runStart);
        int nowSec = Mathf.FloorToInt(now);
        int window = Mathf.Clamp(Plugin.DpsWindowSeconds.Value, 1, 60);
        float span = Mathf.Clamp(now - runStart, 0.5f, window);

        var rows = new List<Row>();
        foreach (var kv in acc)
        {
            var a = kv.Value;
            double recent = 0;
            for (int i = 0; i < 64; i++)
                if (a.BucketSecond[i] > nowSec - window && a.BucketSecond[i] <= nowSec) recent += a.Buckets[i];
            var r = new Row { Id = kv.Key, Color = a.Color, Total = a.Total, Dps = recent / span };
            foreach (var s in a.BySource) r.Sources.Add(new KeyValuePair<int, double>(s.Key, s.Value));
            r.Sources.Sort((x, y) => y.Value.CompareTo(x.Value));
            rows.Add(r);
        }
        rows.Sort((x, y) => y.Total.CompareTo(x.Total));
        Rows = rows;
        HaveData = rows.Count > 0;

        if (Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 1f;
        try
        {
            if (nm.CustomMessagingManager == null || Tracker.ClientCount(nm) <= 1) return;
            string payload = Serialize(rows, RunSeconds);
            var writer = new FastBufferWriter(payload.Length * 2 + 64, Allocator.Temp, -1);
            try
            {
                writer.WriteValueSafe(payload, false);
                nm.CustomMessagingManager.SendNamedMessageToAll(MessageName, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally { writer.Dispose(); }
        }
        catch (Exception ex) { Plugin.Log.LogError($"Sending damage: {ex.Message}"); }
    }

    private static string Serialize(List<Row> rows, float runSeconds)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("1|").Append(Mathf.RoundToInt(runSeconds).ToString(c));
        foreach (var r in rows)
        {
            sb.Append('|').Append(r.Id.ToString(c)).Append(',')
              .Append(ColorUtility.ToHtmlStringRGB(r.Color)).Append(',')
              .Append(Math.Round(r.Total).ToString(c)).Append(',')
              .Append(Math.Round(r.Dps).ToString(c)).Append(',');
            for (int i = 0; i < r.Sources.Count; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(r.Sources[i].Key.ToString(c)).Append(':').Append(Math.Round(r.Sources[i].Value).ToString(c));
            }
        }
        return sb.ToString();
    }

    private static void OnSnapshot(ulong sender, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out string payload, false);
            var c = CultureInfo.InvariantCulture;
            var parts = payload.Split('|');
            if (parts.Length < 2 || parts[0] != "1") return;
            RunSeconds = float.Parse(parts[1], c);
            var rows = new List<Row>();
            for (int i = 2; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                if (f.Length < 5) continue;
                var r = new Row { Id = ulong.Parse(f[0], c), Total = double.Parse(f[2], c), Dps = double.Parse(f[3], c) };
                if (ColorUtility.TryParseHtmlString("#" + f[1], out var col)) r.Color = col;
                if (f[4].Length > 0)
                    foreach (var s in f[4].Split('/'))
                    {
                        int colon = s.IndexOf(':');
                        if (colon > 0) r.Sources.Add(new KeyValuePair<int, double>(int.Parse(s.Substring(0, colon), c), double.Parse(s.Substring(colon + 1), c)));
                    }
                rows.Add(r);
            }
            Rows = rows;
            HaveData = true;
        }
        catch (Exception ex) { Plugin.Log.LogError($"Reading damage snapshot: {ex.Message}"); }
    }

    internal static int ClientCount(NetworkManager nm)
    {
        try
        {
            var list = nm.ConnectedClientsIds?.TryCast<Il2CppSystem.Collections.Generic.List<ulong>>();
            if (list != null) return list.Count;
        }
        catch { }
        return 2; // unknown: assume others are connected
    }

    internal static void Reset()
    {
        acc.Clear();
        Rows = new List<Row>();
        HaveData = false;
        runStart = Time.time;
    }
}
