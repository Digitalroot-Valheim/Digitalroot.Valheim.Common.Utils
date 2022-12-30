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
  [PublicAPI]
  [HarmonyPatch]
  public class VersionCheck
  {
    #region Props

    // ReSharper disable thrice InconsistentNaming
    private static readonly StaticSourceLogger _loggerInstance = new("Digitalroot.ServerSync", true);
    private static readonly string _namespace = $"Digitalroot.Valheim.Common.Config.Providers.ServerSync.{nameof(VersionCheck)}";
    private static readonly HashSet<VersionCheck> _versionChecks = new();
    private static readonly Dictionary<string, string> _notProcessedNames = new();

    public string Name;

    private string? _displayName;

    public string DisplayName
    {
      get => _displayName ?? Name;
      set => _displayName = value;
    }

    private string? _currentVersion;

    public string CurrentVersion
    {
      get => _currentVersion ?? "0.0.0";
      set => _currentVersion = value;
    }

    private string? _minimumRequiredVersion;

    public string MinimumRequiredVersion
    {
      get => _minimumRequiredVersion ?? (ModRequired ? CurrentVersion : "0.0.0");
      set => _minimumRequiredVersion = value;
    }

    public bool ModRequired = true;

    private string? _receivedCurrentVersion;

    private string? _receivedMinimumRequiredVersion;

    // Tracks which clients have passed the version check (only for servers).
    private readonly List<ZRpc> _validatedClients = new();

    // Optional backing field to use ConfigSync values (will override other fields).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "<Pending>")]
    private ConfigSync? _configSync;

    #endregion

    #region Ctors

    static VersionCheck()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}[Pink]");
      typeof(ThreadingHelper).GetMethod(nameof(ThreadingHelper.StartSyncInvoke))!
                             .Invoke(ThreadingHelper.Instance
                                     , new object[]
                                     {
                                       (Action)PatchServerSync
                                     });
    }

    public VersionCheck(string name)
    {
      Name = name;
      ModRequired = true;
      _versionChecks.Add(this);
    }

    public VersionCheck(ConfigSync configSync)
    {
      _configSync = configSync;
      Name = _configSync.Name;
      _versionChecks.Add(this);
    }

    #endregion

    /// <summary>
    /// Init
    /// </summary>
    public void Initialize()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name} _configSync == null : {_configSync == null}");
      _receivedCurrentVersion = null;
      _receivedMinimumRequiredVersion = null;
      if (_configSync == null)
      {
        return;
      }

      Name = _configSync.Name;
      DisplayName = _configSync.DisplayName!;
      CurrentVersion = _configSync.CurrentVersion!;
      MinimumRequiredVersion = _configSync.MinimumRequiredVersion!;
      ModRequired = _configSync.ModRequired;
    }

    /// <summary>
    /// Patches ServerSync into game, patches are applied from class ConfigSync and VersionCheck
    /// </summary>
    private static void PatchServerSync()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}[Pink]");
      if (IsZNetAwakePatched()) return;

      Harmony harmony = new("org.bepinex.helpers.ServerSync");

      var patches = GetPatchesToApply().ToList();
      Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}][Green] patches.Count : {patches.Count}");

      foreach (Type type in patches)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}[Green] ({type.FullName})");
        harmony.PatchAll(type);
      }
    }

    #region Helper Methods

    private static IEnumerable<Type> GetPatchesToApply()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}[Green]");

      var x = typeof(ConfigSync).GetNestedTypes(BindingFlags.NonPublic)
                                .Concat(new[]
                                {
                                  typeof(VersionCheck)
                                }).Where(t => t.IsClass);
      Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}][Green] GetPatchesToApply.Count : {x.Count()}");
      return x;
    }

    private static bool IsZNetAwakePatched()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name} [Pink]");

      var y = PatchProcessor.GetPatchInfo(AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.Awake)))
                            ?.Postfixes.Count(p => p.PatchMethod.DeclaringType == typeof(ConfigSync.RegisterRPCPatch));
      var x = y > 0;

      Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}][Pink] IsZNetAwakePatched : {x}, Count : {y}");

      var readOnlyCollection = PatchProcessor.GetPatchInfo(AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.Awake)))?.Postfixes;
      if (readOnlyCollection != null)
      {
        foreach (var patch in readOnlyCollection)
        {
          Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}[Pink] patch.owner : {patch.owner}");
        }
      }

      return x;
    }

    #endregion

    /// <summary>
    /// Checks if the client and server are running compatible versions.
    /// </summary>
    /// <returns>true|false</returns>
    private bool IsVersionOk()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (_receivedMinimumRequiredVersion == null || _receivedCurrentVersion == null)
      {
        return !ModRequired;
      }

      var myVersionOk = new System.Version(CurrentVersion) >= new System.Version(_receivedMinimumRequiredVersion);
      var otherVersionOk = new System.Version(_receivedCurrentVersion) >= new System.Version(MinimumRequiredVersion);
      return myVersionOk && otherVersionOk;
    }

    /// <summary>
    /// Create error message for when version compability fails. 
    /// </summary>
    /// <returns>Error message for Client</returns>
    private string ErrorClient()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (_receivedMinimumRequiredVersion == null)
      {
        return $"Mod {DisplayName} must not be installed.";
      }

      bool myVersionOk = new System.Version(CurrentVersion) >= new System.Version(_receivedMinimumRequiredVersion);
      return myVersionOk ? $"Mod {DisplayName} requires maximum {_receivedCurrentVersion}. Installed is version {CurrentVersion}." : $"Mod {DisplayName} requires minimum {_receivedMinimumRequiredVersion}. Installed is version {CurrentVersion}.";
    }

    /// <summary>
    /// Create error message for when version compability fails. 
    /// </summary>
    /// <returns>Error message for Server</returns>
    private string ErrorServer(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return $"Disconnect: The client ({rpc.GetSocket().GetHostName()}) doesn't have the correct {DisplayName} version {MinimumRequiredVersion}";
    }

    /// <summary>
    /// Create error message for when version compability fails. 
    /// </summary>
    /// <param name="rpc">Error message for Client|Server</param>
    /// <returns></returns>
    private string Error(ZRpc? rpc = null)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return rpc == null ? ErrorClient() : ErrorServer(rpc);
    }

    /// <summary>
    /// Get collection of mods with failed version validation. 
    /// </summary>
    /// <returns>Collection of mods that failed version validation.</returns>
    private static VersionCheck[] GetFailedClient()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return _versionChecks.Where(check => !check.IsVersionOk()).ToArray();
    }


    /// <summary>
    /// Get collection of mods with failed version validation. 
    /// </summary>
    /// <param name="rpc"></param>
    /// <returns>Collection of mods that failed version validation.</returns>
    private static VersionCheck[] GetFailedServer(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      return _versionChecks.Where(check => check.ModRequired && !check._validatedClients.Contains(rpc)).ToArray();
    }

    /// <summary>
    /// Force Game to logout in an error state.
    /// </summary>
    private static void Logout()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      Game.instance.Logout();
      AccessTools.DeclaredField(typeof(ZNet), nameof(ZNet.m_connectionStatus)).SetValue(null, ZNet.ConnectionStatus.ErrorVersion);
    }

    /// <summary>
    /// Disconnects the Client.
    /// </summary>
    /// <param name="rpc"></param>
    private static void DisconnectClient(ZRpc rpc)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      rpc.Invoke(nameof(Error), (int)ZNet.ConnectionStatus.ErrorVersion);
    }

    /// <summary>
    /// Wrapper for CheckVersion
    /// </summary>
    /// <param name="rpc"></param>
    /// <param name="pkg"></param>
    private static void CheckVersion(ZRpc rpc, ZPackage pkg) => CheckVersion(rpc, pkg, null);

    /// <summary>
    /// Validate mod versions.
    /// </summary>
    /// <param name="rpc"></param>
    /// <param name="pkg"></param>
    /// <param name="original"></param>
    private static void CheckVersion(ZRpc rpc, ZPackage pkg, Action<ZRpc, ZPackage>? original)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      var guid = pkg.ReadString();
      var minimumRequiredVersion = pkg.ReadString();
      var currentVersion = pkg.ReadString();

      var matched = false;

      foreach (var check in _versionChecks)
      {
        if (guid != check.Name)
        {
          continue;
        }

        Log.Debug(_loggerInstance, $"Received {check.DisplayName} version {currentVersion} and minimum version {minimumRequiredVersion} from the {(ZNet.instance.IsServer() ? "client" : "server")}.");

        check._receivedMinimumRequiredVersion = minimumRequiredVersion;
        check._receivedCurrentVersion = currentVersion;
        if (ZNet.instance.IsServer() && check.IsVersionOk())
        {
          check._validatedClients.Add(rpc);
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
            _notProcessedNames.Add(guid, currentVersion);
            Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}] _notProcessedNames.Add(guid, currentVersion), guid : {guid}, currentVersion : {currentVersion}");
          }
        }
      }
    }

    /// <summary>
    /// Check for failed validation.
    /// On failed validation:
    ///   Client -> Logout()
    ///   Server -> Disconnect()
    /// </summary>
    /// <param name="rpc"></param>
    /// <param name="__instance"></param>
    /// <returns></returns>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo)), HarmonyPrefix]
    private static bool RPC_PeerInfo(ZRpc rpc, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      var failedChecks = __instance.IsServer() ? GetFailedServer(rpc) : GetFailedClient();
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name} failedChecks.Length : {failedChecks.Length}");
      if (failedChecks.Length == 0)
      {
        return true;
      }

      foreach (var check in failedChecks)
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

    /// <summary>
    /// Register RPCs and check mod versions.
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="__instance"></param>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection)), HarmonyPrefix]
    private static void RegisterAndCheckVersion(ZNetPeer peer, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      _notProcessedNames.Clear();

      var rpcFunctions = (IDictionary)typeof(ZRpc).GetField(nameof(ZRpc.m_functions), BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer.m_rpc);
      if (rpcFunctions.Contains("ServerSync VersionCheck".GetStableHashCode()))
      {
        var function = rpcFunctions["ServerSync VersionCheck".GetStableHashCode()];
        Action<ZRpc, ZPackage> action = (Action<ZRpc, ZPackage>)function.GetType().GetField("m_action", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(function);
        peer.m_rpc.Register<ZPackage>("ServerSync VersionCheck", (rpc, pkg) => CheckVersion(rpc, pkg, action));
      }
      else
      {
        peer.m_rpc.Register<ZPackage>("ServerSync VersionCheck", CheckVersion);
      }

      foreach (var check in _versionChecks)
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

    /// <summary>
    /// Clean up server's client list when a client disconnects.
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="__instance"></param>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect)), HarmonyPrefix]
    private static void RemoveDisconnected(ZNetPeer peer, ZNet __instance)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (!__instance.IsServer())
      {
        return;
      }

      foreach (VersionCheck check in _versionChecks)
      {
        check._validatedClients.Remove(peer.m_rpc);
      }
    }

    /// <summary>
    /// Show Error on client when disconnected
    /// </summary>
    /// <param name="__instance"></param>
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

      foreach (KeyValuePair<string, string> kv in _notProcessedNames.OrderBy(kv => kv.Key))
      {
        if (!__instance.m_connectionFailedError.text.Contains(kv.Key))
        {
          __instance.m_connectionFailedError.text += $"\n{kv.Key} (Version: {kv.Value})";
        }
      }
    }
  }
}
