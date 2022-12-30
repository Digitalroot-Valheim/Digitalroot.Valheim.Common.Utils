#nullable enable
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Digitalroot.Valheim.Common.Config.Providers.ServerSync
{
  #region ServerSync

  // internal class ConfigurationManagerAttributes
  // {
  //   [UsedImplicitly]
  //   public bool? ReadOnly = false;
  // }

  [PublicAPI]
  [HarmonyPatch]
  public class VersionCheck
  {
    private static readonly StaticSourceLogger _loggerInstance = StaticSourceLogger.PreMadeTraceableInstance;
    private static string _namespace = $"Digitalroot.Valheim.Common.Config.Providers.ServerSync.{nameof(VersionCheck)}";
    private static readonly HashSet<VersionCheck> versionChecks = new();
    private static readonly Dictionary<string, string> notProcessedNames = new();

    public string Name;

    private string? displayName;

    public string DisplayName
    {
      get => displayName ?? Name;
      set => displayName = value;
    }

    private string? currentVersion;

    public string CurrentVersion
    {
      get => currentVersion ?? "0.0.0";
      set => currentVersion = value;
    }

    private string? minimumRequiredVersion;

    public string MinimumRequiredVersion
    {
      get => minimumRequiredVersion ?? (ModRequired ? CurrentVersion : "0.0.0");
      set => minimumRequiredVersion = value;
    }

    public bool ModRequired = true;

    private string? ReceivedCurrentVersion;

    private string? ReceivedMinimumRequiredVersion;

    // Tracks which clients have passed the version check (only for servers).
    private readonly List<ZRpc> ValidatedClients = new();

    // Optional backing field to use ConfigSync values (will override other fields).
    private ConfigSync? ConfigSync;

    private static void PatchServerSync()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (PatchProcessor.GetPatchInfo(AccessTools.DeclaredMethod(typeof(ZNet), "Awake"))?.Postfixes.Count(p => p.PatchMethod.DeclaringType == typeof(ConfigSync.RegisterRPCPatch)) > 0)
      {
        return;
      }

      Harmony harmony = new("org.bepinex.helpers.ServerSync");
      foreach (Type type in typeof(ConfigSync).GetNestedTypes(BindingFlags.NonPublic).Concat(new[] { typeof(VersionCheck) }).Where(t => t.IsClass))
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name} ({type.FullName})");
        harmony.PatchAll(type);
      }
    }

    static VersionCheck()
    {
      typeof(ThreadingHelper).GetMethod("StartSyncInvoke")!.Invoke(ThreadingHelper.Instance, new object[] { (Action)PatchServerSync });
    }

    public VersionCheck(string name)
    {
      Name = name;
      ModRequired = true;
      versionChecks.Add(this);
    }

    public VersionCheck(ConfigSync configSync)
    {
      ConfigSync = configSync;
      Name = ConfigSync.Name;
      versionChecks.Add(this);
    }

    public void Initialize()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      ReceivedCurrentVersion = null;
      ReceivedMinimumRequiredVersion = null;
      if (ConfigSync == null)
      {
        return;
      }

      Name = ConfigSync.Name;
      DisplayName = ConfigSync.DisplayName!;
      CurrentVersion = ConfigSync.CurrentVersion!;
      MinimumRequiredVersion = ConfigSync.MinimumRequiredVersion!;
      ModRequired = ConfigSync.ModRequired;
    }

    private bool IsVersionOk()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (ReceivedMinimumRequiredVersion == null || ReceivedCurrentVersion == null)
      {
        return !ModRequired;
      }

      bool myVersionOk = new System.Version(CurrentVersion) >= new System.Version(ReceivedMinimumRequiredVersion);
      bool otherVersionOk = new System.Version(ReceivedCurrentVersion) >= new System.Version(MinimumRequiredVersion);
      return myVersionOk && otherVersionOk;
    }

    private string ErrorClient()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (ReceivedMinimumRequiredVersion == null)
      {
        return $"Mod {DisplayName} must not be installed.";
      }

      bool myVersionOk = new System.Version(CurrentVersion) >= new System.Version(ReceivedMinimumRequiredVersion);
      return myVersionOk ? $"Mod {DisplayName} requires maximum {ReceivedCurrentVersion}. Installed is version {CurrentVersion}." : $"Mod {DisplayName} requires minimum {ReceivedMinimumRequiredVersion}. Installed is version {CurrentVersion}.";
    }

    private string ErrorServer(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return $"Disconnect: The client ({rpc.GetSocket().GetHostName()}) doesn't have the correct {DisplayName} version {MinimumRequiredVersion}";
    }

    private string Error(ZRpc? rpc = null)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return rpc == null ? ErrorClient() : ErrorServer(rpc);
    }

    private static VersionCheck[] GetFailedClient()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return versionChecks.Where(check => !check.IsVersionOk()).ToArray();
    }

    private static VersionCheck[] GetFailedServer(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return versionChecks.Where(check => check.ModRequired && !check.ValidatedClients.Contains(rpc)).ToArray();
    }

    private static void Logout()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      Game.instance.Logout();
      AccessTools.DeclaredField(typeof(ZNet), "m_connectionStatus").SetValue(null, ZNet.ConnectionStatus.ErrorVersion);
    }

    private static void DisconnectClient(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
    }

    private static void CheckVersion(ZRpc rpc, ZPackage pkg) => CheckVersion(rpc, pkg, null);

    private static void CheckVersion(ZRpc rpc, ZPackage pkg, Action<ZRpc, ZPackage>? original)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      string guid = pkg.ReadString();
      string minimumRequiredVersion = pkg.ReadString();
      string currentVersion = pkg.ReadString();

      bool matched = false;

      foreach (VersionCheck check in versionChecks)
      {
        if (guid != check.Name)
        {
          continue;
        }

        Log.Debug(_loggerInstance, $"Received {check.DisplayName} version {currentVersion} and minimum version {minimumRequiredVersion} from the {(ZNet.instance.IsServer() ? "client" : "server")}.");

        check.ReceivedMinimumRequiredVersion = minimumRequiredVersion;
        check.ReceivedCurrentVersion = currentVersion;
        if (ZNet.instance.IsServer() && check.IsVersionOk())
        {
          check.ValidatedClients.Add(rpc);
        }

        matched = true;
      }

      if (!matched)
      {
        pkg.SetPos(0);
        if (original is not null)
        {
          original(rpc, pkg);
          if (pkg.GetPos() == 0)
          {
            notProcessedNames.Add(guid, currentVersion);
          }
        }
      }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo)), HarmonyPrefix]
    private static bool RPC_PeerInfo(ZRpc rpc, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      VersionCheck[] failedChecks = __instance.IsServer() ? GetFailedServer(rpc) : GetFailedClient();
      if (failedChecks.Length == 0)
      {
        return true;
      }

      foreach (VersionCheck check in failedChecks)
      {
        Log.Warning(_loggerInstance, check.Error(rpc));
      }

      if (__instance.IsServer())
      {
        DisconnectClient(rpc);
      }
      else
      {
        Logout();
      }

      return false;
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection)), HarmonyPrefix]
    private static void RegisterAndCheckVersion(ZNetPeer peer, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      notProcessedNames.Clear();

      IDictionary rpcFunctions = (IDictionary)typeof(ZRpc).GetField("m_functions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer.m_rpc);
      if (rpcFunctions.Contains("ServerSync VersionCheck".GetStableHashCode()))
      {
        object function = rpcFunctions["ServerSync VersionCheck".GetStableHashCode()];
        Action<ZRpc, ZPackage> action = (Action<ZRpc, ZPackage>)function.GetType().GetField("m_action", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(function);
        peer.m_rpc.Register<ZPackage>("ServerSync VersionCheck", (rpc, pkg) => CheckVersion(rpc, pkg, action));
      }
      else
      {
        peer.m_rpc.Register<ZPackage>("ServerSync VersionCheck", CheckVersion);
      }

      foreach (VersionCheck check in versionChecks)
      {
        check.Initialize();
        // If the mod is not required, then it's enough for only one side to do the check.
        if (!check.ModRequired && !__instance.IsServer())
        {
          continue;
        }

        Log.Debug(_loggerInstance, $"Sending {check.DisplayName} version {check.CurrentVersion} and minimum version {check.MinimumRequiredVersion} to the {(__instance.IsServer() ? "client" : "server")}.");

        ZPackage zpackage = new();
        zpackage.Write(check.Name);
        zpackage.Write(check.MinimumRequiredVersion);
        zpackage.Write(check.CurrentVersion);
        peer.m_rpc.Invoke("ServerSync VersionCheck", zpackage);
      }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect)), HarmonyPrefix]
    private static void RemoveDisconnected(ZNetPeer peer, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (!__instance.IsServer())
      {
        return;
      }

      foreach (VersionCheck check in versionChecks)
      {
        check.ValidatedClients.Remove(peer.m_rpc);
      }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError)), HarmonyPostfix]
    private static void ShowConnectionError(FejdStartup __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (!__instance.m_connectionFailedPanel.activeSelf || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorVersion)
      {
        return;
      }

      VersionCheck[] failedChecks = GetFailedClient();
      if (failedChecks.Length > 0)
      {
        string error = string.Join("\n", failedChecks.Select(check => check.Error()));
        __instance.m_connectionFailedError.text += "\n" + error;
      }

      foreach (KeyValuePair<string, string> kv in notProcessedNames.OrderBy(kv => kv.Key))
      {
        if (!__instance.m_connectionFailedError.text.Contains(kv.Key))
        {
          __instance.m_connectionFailedError.text += $"\n{kv.Key} (Version: {kv.Value})";
        }
      }
    }
  }

  #endregion
}
