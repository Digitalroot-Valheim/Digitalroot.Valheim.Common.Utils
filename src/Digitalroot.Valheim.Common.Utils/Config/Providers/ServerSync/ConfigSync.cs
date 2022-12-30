#nullable enable
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Digitalroot.Valheim.Common.Config.Providers.ServerSync
{
  [PublicAPI]
  public class ConfigSync
  {
    #region Props

    // ReSharper disable twice InconsistentNaming
    private static readonly StaticSourceLogger _loggerInstance = new("Digitalroot.ServerSync", true);
    private static readonly string _namespace = $"Digitalroot.Valheim.Common.Config.Providers.{nameof(ConfigSync)}";
    private static readonly HashSet<ConfigSync> _configSyncs = new();
    private static bool _isServer;
    private readonly HashSet<OwnConfigEntryBase> _allConfigs = new();
    private readonly HashSet<CustomSyncedValueBase> _allCustomValues = new();

    public static bool ProcessingServerUpdate;
    public readonly string Name;
    public string? DisplayName;
    public string? CurrentVersion;
    public string? MinimumRequiredVersion;
    public bool ModRequired;

    #region Source of Truth

    private bool _isSourceOfTruth = true;

    public bool IsSourceOfTruth
    {
      get => _isSourceOfTruth;
      private set
      {
        if (value != _isSourceOfTruth)
        {
          _isSourceOfTruth = value;
          SourceOfTruthChanged?.Invoke(value);
        }
      }
    }

    public event Action<bool>? SourceOfTruthChanged;

    #endregion

    #region Locking

    public bool IsLocked
    {
      get => (_forceConfigLocking ?? _lockedConfig != null && ((IConvertible)_lockedConfig.BaseConfig.BoxedValue).ToInt32(CultureInfo.InvariantCulture) != 0) && !_lockExempt;
      set => _forceConfigLocking = value;
    }

    public bool IsAdmin => _lockExempt;

    private bool? _forceConfigLocking;
    private static bool _lockExempt;

    private OwnConfigEntryBase? _lockedConfig;
    private event Action? _lockedConfigChanged;

    #endregion

    #endregion

    #region Ctors

    static ConfigSync()
    {
      RuntimeHelpers.RunClassConstructor(typeof(VersionCheck).TypeHandle);
    }

    public ConfigSync(string name)
    {
      Name = name;
      _configSyncs.Add(this);
      _ = new VersionCheck(this);
    }

    #endregion

    /// <summary>
    /// Adds a ConfigEntry to sync. If ConfigEntry has already been added, the existing ref to it is returned.
    /// Also adds evert handler for OnSettingChanged.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="configEntry"></param>
    /// <returns></returns>
    public SyncedConfigEntry<T> AddConfigEntry<T>(ConfigEntry<T> configEntry)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}(Key : {configEntry.Definition.Key}, Section : {configEntry.Definition.Section}, Value : {configEntry.Value}), Description : {configEntry.Description.Description})");
      if (ConfigData((ConfigEntryBase)configEntry) is SyncedConfigEntry<T> syncedEntry) return syncedEntry;

      syncedEntry = new SyncedConfigEntry<T>(configEntry);
      AccessTools.DeclaredField(typeof(ConfigDescription), "<Tags>k__BackingField").SetValue(configEntry.Description, (new object[] { new ConfigurationManagerAttributes() }).Concat(configEntry.Description.Tags ?? Array.Empty<object>()).Concat(new[] { syncedEntry }).ToArray());
      configEntry.SettingChanged += (_, _) =>
                                    {
                                      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
                                      if (!ProcessingServerUpdate && syncedEntry.SynchronizedConfig)
                                      {
                                        Broadcast(ZRoutedRpc.Everybody, configEntry);
                                      }
                                    };
      _allConfigs.Add(syncedEntry);

      return syncedEntry;
    }

    /// <summary>
    /// Adds a ConfigEntry to use and sync as the Locking config. If ConfigEntry has already been added an exception is thrown.
    /// Also adds evert handler for OnSettingChanged which bubbles up to _lockedConfigChanged event.
    /// </summary>
    /// <typeparam name="T">IConvertible</typeparam>
    /// <param name="lockingConfig"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public SyncedConfigEntry<T> AddLockingConfigEntry<T>(ConfigEntry<T> lockingConfig)
      where T : IConvertible
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}(Key : {lockingConfig.Definition.Key}, Section : {lockingConfig.Definition.Section}, Value : {lockingConfig.Value}), Description : {lockingConfig.Description.Description})");
      if (_lockedConfig != null)
      {
        throw new Exception("Cannot initialize locking ConfigEntry twice");
      }

      _lockedConfig = AddConfigEntry(lockingConfig);
      lockingConfig.SettingChanged += (_, _) => _lockedConfigChanged?.Invoke();

      return (SyncedConfigEntry<T>)_lockedConfig;
    }

    // ReSharper disable once CommentTypo
    /// <summary>
    /// Adds a CustomSyncedValue to sync. Name must be unique and the name 'serverversion' is reserved.
    /// Also adds evert handler for OnValueChanged.
    /// </summary>
    /// <param name="customValue"></param>
    /// <exception cref="Exception"></exception>
    public void AddCustomValue(CustomSyncedValueBase customValue)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}(Identifier : {customValue.Identifier}, Value : {customValue.BoxedValue})");
      if (_allCustomValues.Select(v => v.Identifier)
                          .Concat(new[]
                          {
                            "serverversion"
                          })
                          .Contains(customValue.Identifier))
      {
        throw new Exception("Cannot have multiple settings with the same name or with a reserved name (serverversion)");
      }

      _allCustomValues.Add(customValue);
      customValue.ValueChanged += () =>
                                  {
                                    if (!ProcessingServerUpdate)
                                    {
                                      Broadcast(ZRoutedRpc.Everybody, customValue);
                                    }
                                  };
    }

    #region Patches

    [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.HandlePackage))]
    private static class SnatchCurrentlyHandlingRPC
    {
      public static ZRpc? currentRpc;

      [HarmonyPrefix]
      private static void Prefix(ZRpc __instance) => currentRpc = __instance;
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class RegisterRPCPatch
    {
      [HarmonyPostfix]
      private static void Postfix(ZNet __instance)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");

        _isServer = __instance.IsServer();
        foreach (var configSync in _configSyncs)
        {
          configSync.IsSourceOfTruth = __instance.IsDedicated() || __instance.IsServer();
          ZRoutedRpc.instance.Register<ZPackage>(configSync.Name + " ConfigSync", configSync.RPC_ConfigSync);
          if (_isServer)
          {
            Log.Debug(_loggerInstance, $"Registered '{configSync.Name} ConfigSync' RPC - waiting for incoming connections");
          }
        }

        // ReSharper disable once IdentifierTypo
        IEnumerator WatchAdminListChangesCoroutine()
        {
          var adminList = (SyncedList)AccessTools.DeclaredField(typeof(ZNet), nameof(ZNet.m_adminList)).GetValue(ZNet.instance);
          List<string> CurrentList = new(adminList.GetList());
          for (;;)
          {
            yield return new WaitForSeconds(30);
            if (adminList.GetList().SequenceEqual(CurrentList)) continue;

            CurrentList = new List<string>(adminList.GetList());

            void SendAdmin(List<ZNetPeer> peers, bool isAdmin)
            {
              var package = ConfigsToPackage(packageEntries: new[]
              {
                new PackageEntry { section = "Internal", key = "lockexempt", type = typeof(bool), value = isAdmin }
              });

              if (_configSyncs.First() is { } configSync)
              {
                ZNet.instance.StartCoroutine(configSync.SendZPackage(peers, package));
              }
            }

            List<ZNetPeer> adminPeer = ZNet.instance.GetPeers().Where(p => adminList.Contains(p.m_rpc.GetSocket().GetHostName())).ToList();
            List<ZNetPeer> nonAdminPeer = ZNet.instance.GetPeers().Except(adminPeer).ToList();
            SendAdmin(nonAdminPeer, false);
            SendAdmin(adminPeer, true);
          }
          // ReSharper disable once IteratorNeverReturns
        }

        if (_isServer)
        {
          __instance.StartCoroutine(WatchAdminListChangesCoroutine());
        }
      }
    }

    /// <summary>
    /// Send configs to Client when it connects.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    private static class RegisterClientRPCPatch
    {
      [HarmonyPostfix]
      private static void Postfix(ZNet __instance, ZNetPeer peer)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name} __instance.IsServer() : {__instance.IsServer()}");
        if (__instance.IsServer()) return;

        foreach (var configSync in _configSyncs)
        {
          Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}] configSync.Name : {configSync.Name}");
          peer.m_rpc.Register<ZPackage>(configSync.Name + " ConfigSync", configSync.RPC_InitialConfigSync);
        }
      }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    private class ResetConfigsOnShutdown
    {
      [HarmonyPostfix]
      private static void Postfix()
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        ProcessingServerUpdate = true;
        foreach (var serverSync in _configSyncs)
        {
          serverSync.ResetConfigsFromServer();
        }

        ProcessingServerUpdate = false;
      }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
    private class SendConfigsAfterLogin
    {
      private class BufferingSocket : ISocket
      {
        public volatile bool finished;
        public volatile int versionMatchQueued = -1;
        public readonly List<ZPackage> Package = new();
        public readonly ISocket Original;

        public BufferingSocket(ISocket original)
        {
          Original = original;
        }

        public bool IsConnected() => Original.IsConnected();
        public ZPackage Recv() => Original.Recv();
        public int GetSendQueueSize() => Original.GetSendQueueSize();
        public int GetCurrentSendRate() => Original.GetCurrentSendRate();
        public bool IsHost() => Original.IsHost();
        public void Dispose() => Original.Dispose();
        public bool GotNewData() => Original.GotNewData();
        public void Close() => Original.Close();
        public string GetEndPointString() => Original.GetEndPointString();
        public void GetAndResetStats(out int totalSent, out int totalRecv) => Original.GetAndResetStats(out totalSent, out totalRecv);
        public void GetConnectionQuality(out float localQuality, out float remoteQuality, out int ping, out float outByteSec, out float inByteSec) => Original.GetConnectionQuality(out localQuality, out remoteQuality, out ping, out outByteSec, out inByteSec);
        public ISocket Accept() => Original.Accept();
        public int GetHostPort() => Original.GetHostPort();
        public bool Flush() => Original.Flush();
        public string GetHostName() => Original.GetHostName();

        public void VersionMatch()
        {
          Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
          if (finished)
          {
            Original.VersionMatch();
          }
          else
          {
            versionMatchQueued = Package.Count;
          }
        }

        public void Send(ZPackage pkg)
        {
          Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
          var oldPos = pkg.GetPos();
          pkg.SetPos(0);
          var methodHash = pkg.ReadInt();
          if ((methodHash == "PeerInfo".GetStableHashCode() || methodHash == "RoutedRPC".GetStableHashCode() || methodHash == "ZDOData".GetStableHashCode()) && !finished)
          {
            ZPackage newPkg = new(pkg.GetArray());
            newPkg.SetPos(oldPos);
            Package.Add(newPkg); // the original ZPackage gets reused, create a new one
          }
          else
          {
            pkg.SetPos(oldPos);
            Original.Send(pkg);
          }
        }
      }

      [HarmonyPriority(Priority.First)]
      [HarmonyPrefix]
      private static void Prefix(ref Dictionary<Assembly, BufferingSocket>? __state, ZNet __instance, ZRpc rpc)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        if (!__instance.IsServer()) return;

        BufferingSocket bufferingSocket = new(rpc.GetSocket());
        AccessTools.DeclaredField(typeof(ZRpc), nameof(ZRpc.m_socket)).SetValue(rpc, bufferingSocket);
        // Don't replace on steam sockets, RPC_PeerInfo does peer.m_socket as ZSteamSocket - which will cause a nullref when replaced
        if (AccessTools.DeclaredMethod(typeof(ZNet)
                                       , nameof(ZNet.GetPeer)
                                       , new[]
                                       {
                                         typeof(ZRpc)
                                       }).Invoke(__instance
                                                 , new object[]
                                                 {
                                                   rpc
                                                 }) is ZNetPeer peer
            && ZNet.m_onlineBackend != OnlineBackendType.Steamworks)
        {
          AccessTools.DeclaredField(typeof(ZNetPeer), nameof(ZNetPeer.m_socket)).SetValue(peer, bufferingSocket);
        }

        __state ??= new Dictionary<Assembly, BufferingSocket>();
        __state[Assembly.GetExecutingAssembly()] = bufferingSocket;
      }

      [HarmonyPostfix]
      private static void Postfix(Dictionary<Assembly, BufferingSocket> __state, ZNet __instance, ZRpc rpc)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        if (!__instance.IsServer()) return;

        void SendBufferedData()
        {
          if (rpc.GetSocket() is BufferingSocket bufferingSocket)
          {
            AccessTools.DeclaredField(typeof(ZRpc), nameof(ZRpc.m_socket)).SetValue(rpc, bufferingSocket.Original);
            if (AccessTools.DeclaredMethod(typeof(ZNet)
                                           , nameof(ZNet.GetPeer)
                                           , new[]
                                           {
                                             typeof(ZRpc)
                                           }).Invoke(__instance
                                                     , new object[]
                                                     {
                                                       rpc
                                                     }) is ZNetPeer zNetPeer)
            {
              AccessTools.DeclaredField(typeof(ZNetPeer), nameof(ZNetPeer.m_socket)).SetValue(zNetPeer, bufferingSocket.Original);
            }
          }

          bufferingSocket = __state[Assembly.GetExecutingAssembly()];
          bufferingSocket.finished = true;

          for (var i = 0; i < bufferingSocket.Package.Count; ++i)
          {
            if (i == bufferingSocket.versionMatchQueued)
            {
              bufferingSocket.Original.VersionMatch();
            }

            bufferingSocket.Original.Send(bufferingSocket.Package[i]);
          }

          if (bufferingSocket.Package.Count == bufferingSocket.versionMatchQueued)
          {
            bufferingSocket.Original.VersionMatch();
          }
        }

        if (AccessTools.DeclaredMethod(typeof(ZNet)
                                       , nameof(ZNet.GetPeer)
                                       , new[]
                                       {
                                         typeof(ZRpc)
                                       }).Invoke(__instance
                                                 , new object[]
                                                 {
                                                   rpc
                                                 }) is not ZNetPeer peer)
        {
          SendBufferedData();
          return;
        }

        IEnumerator SendAsyncCoroutine()
        {
          foreach (var configSync in _configSyncs)
          {
            List<PackageEntry> entries = new();
            if (configSync.CurrentVersion != null)
            {
              entries.Add(new PackageEntry
              {
                section = "Internal"
                // ReSharper disable once StringLiteralTypo
                , key = "serverversion"
                , type = typeof(string)
                , value = configSync.CurrentVersion
              });
            }

            var listContainsId = AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.ListContainsId));
            var adminList = (SyncedList)AccessTools.DeclaredField(typeof(ZNet), nameof(ZNet.m_adminList)).GetValue(ZNet.instance);
            entries.Add(new PackageEntry
            {
              section = "Internal"
              // ReSharper disable once StringLiteralTypo
              , key = "lockexempt"
              , type = typeof(bool)
              , value = listContainsId is null
                          ? adminList.Contains(rpc.GetSocket().GetHostName())
                          : listContainsId.Invoke(ZNet.instance
                                                  , new object[]
                                                  {
                                                    adminList
                                                    , rpc.GetSocket().GetHostName()
                                                  })
            });

            var package = ConfigsToPackage(configSync._allConfigs
                                                     .Select(c => c.BaseConfig)
                                           , configSync._allCustomValues
                                           , entries
                                           , false);

            yield return __instance.StartCoroutine(configSync.SendZPackage(new List<ZNetPeer>
            {
              peer
            }, package));
          }

          SendBufferedData();
        }

        __instance.StartCoroutine(SendAsyncCoroutine());
      }
    }

    [HarmonyPatch(typeof(ConfigEntryBase), nameof(ConfigEntryBase.GetSerializedValue))]
    private static class PreventSavingServerInfo
    {
      [HarmonyPrefix]
      private static bool Prefix(ConfigEntryBase __instance, ref string __result)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        if (ConfigData(__instance) is not { } data || IsWritableConfig(data))
        {
          return true;
        }

        __result = TomlTypeConverter.ConvertToString(data.LocalBaseValue, __instance.SettingType);
        return false;
      }
    }

    [HarmonyPatch(typeof(ConfigEntryBase), nameof(ConfigEntryBase.SetSerializedValue))]
    private static class PreventConfigRereadChangingValues
    {
      [HarmonyPrefix]
      private static bool Prefix(ConfigEntryBase __instance, string value)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        if (ConfigData(__instance) is not { } data || data.LocalBaseValue == null)
        {
          return true;
        }

        try
        {
          data.LocalBaseValue = TomlTypeConverter.ConvertToValue(value, __instance.SettingType);
        }
        catch (Exception e)
        {
          Log.Warning(_loggerInstance, $"Config value of setting \"{__instance.Definition}\" could not be parsed and will be ignored. Reason: {e.Message}; Value: {value}");
        }

        return false;
      }
    }

    #endregion

    private const byte PARTIAL_CONFIGS = 1;
    private const byte FRAGMENTED_CONFIG = 2;
    private const byte COMPRESSED_CONFIG = 4;

    private readonly Dictionary<string, SortedDictionary<int, byte[]>> configValueCache = new();
    private readonly List<KeyValuePair<long, string>> cacheExpirations = new(); // avoid leaking memory

    /// <summary>
    /// Initial syncing of the config values.
    /// </summary>
    /// <param name="rpc"></param>
    /// <param name="package"></param>
    private void RPC_InitialConfigSync(ZRpc rpc, ZPackage package) => RPC_ConfigSync(0, package);

    /// <summary>
    /// Reads the config values from the ZPackage.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="package"></param>
    private void RPC_ConfigSync(long sender, ZPackage package)
    {
      try
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}] _isServer : {_isServer}, IsLocked : {IsLocked}");
        if (_isServer && IsLocked)
        {
          var exempt = ((SyncedList?)AccessTools.DeclaredField(typeof(ZNet), nameof(ZNet.m_adminList)).GetValue(ZNet.instance))?.Contains(SnatchCurrentlyHandlingRPC.currentRpc?.GetSocket()?.GetHostName());
          Log.Trace(_loggerInstance, $"[{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}] exempt : {exempt}");
          if (exempt == false)
          {
            return;
          }
        }

        cacheExpirations.RemoveAll(kv =>
                                   {
                                     if (kv.Key >= DateTimeOffset.Now.Ticks) return false;
                                     configValueCache.Remove(kv.Value);
                                     return true;
                                   }
                                  );

        var packageFlags = package.ReadByte();

        if ((packageFlags & FRAGMENTED_CONFIG) != 0)
        {
          var uniqueIdentifier = package.ReadLong();
          var cacheKey = sender.ToString() + uniqueIdentifier;
          if (!configValueCache.TryGetValue(cacheKey, out var dataFragments))
          {
            dataFragments = new SortedDictionary<int, byte[]>();
            configValueCache[cacheKey] = dataFragments;
            cacheExpirations.Add(new KeyValuePair<long, string>(DateTimeOffset.Now.AddSeconds(60).Ticks, cacheKey));
          }

          var fragment = package.ReadInt();
          var fragments = package.ReadInt();

          dataFragments.Add(fragment, package.ReadByteArray());

          if (dataFragments.Count < fragments)
          {
            return;
          }

          configValueCache.Remove(cacheKey);

          package = new ZPackage(dataFragments.Values.SelectMany(a => a).ToArray());
          packageFlags = package.ReadByte();
        }

        ProcessingServerUpdate = true;

        if ((packageFlags & COMPRESSED_CONFIG) != 0)
        {
          var data = package.ReadByteArray();

          MemoryStream input = new(data);
          MemoryStream output = new();
          using (DeflateStream deflateStream = new(input, CompressionMode.Decompress))
          {
            deflateStream.CopyTo(output);
          }

          package = new ZPackage(output.ToArray());
          packageFlags = package.ReadByte();
        }

        if ((packageFlags & PARTIAL_CONFIGS) == 0)
        {
          ResetConfigsFromServer();
        }

        if (!_isServer)
        {
          if (IsSourceOfTruth)
          {
            _lockedConfigChanged += ServerLockedSettingChanged;
          }

          IsSourceOfTruth = false;
        }

        var configs = ReadConfigsFromPackage(package);

        foreach (var configKv in configs.configValues)
        {
          if (!_isServer && configKv.Key.LocalBaseValue == null)
          {
            configKv.Key.LocalBaseValue = configKv.Key.BaseConfig.BoxedValue;
          }

          configKv.Key.BaseConfig.BoxedValue = configKv.Value;
        }

        foreach (var configKv in configs.customValues)
        {
          if (!_isServer)
          {
            configKv.Key.LocalBaseValue ??= configKv.Key.BoxedValue;
          }

          configKv.Key.BoxedValue = configKv.Value;
        }

        if (_isServer) return;

        Log.Debug(_loggerInstance, $"Received {configs.configValues.Count} configs and {configs.customValues.Count} custom values from the server for mod {DisplayName ?? Name}");

        ServerLockedSettingChanged(); // Re-evaluate for intial locking
      }
      finally
      {
        ProcessingServerUpdate = false;
      }
    }

    /// <summary>
    /// Stores configs and custom values.
    /// </summary>
    private class ParsedConfigs
    {
      public readonly Dictionary<OwnConfigEntryBase, object?> configValues = new();
      public readonly Dictionary<CustomSyncedValueBase, object?> customValues = new();
    }

    /// <summary>
    /// Parse ZPackage
    /// </summary>
    /// <param name="package"></param>
    /// <returns></returns>
    private ParsedConfigs ReadConfigsFromPackage(ZPackage package)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      ParsedConfigs configs = new();
      var configMap = _allConfigs.Where(c => c.SynchronizedConfig).ToDictionary(c => c.BaseConfig.Definition.Section + "_" + c.BaseConfig.Definition.Key, c => c);

      var customValueMap = _allCustomValues.ToDictionary(c => c.Identifier, c => c);

      var valueCount = package.ReadInt();
      for (var i = 0; i < valueCount; ++i)
      {
        var groupName = package.ReadString();
        var configName = package.ReadString();
        var typeName = package.ReadString();

        var type = Type.GetType(typeName);
        if (typeName == "" || type != null)
        {
          object? value;
          try
          {
            value = typeName == "" ? null : ReadValueWithTypeFromZPackage(package, type!);
          }
          catch (InvalidDeserializationTypeException e)
          {
            Log.Warning(_loggerInstance, $"Got unexpected struct internal type {e.received} for field {e.field} struct {typeName} for {configName} in section {groupName} for mod {DisplayName ?? Name}, expecting {e.expected}");
            continue;
          }

          HandleGroups(groupName, configName, value, customValueMap, typeName, configs, configMap);
        }
        else
        {
          Log.Warning(_loggerInstance, $"Got invalid type {typeName}, abort reading of received configs");
          return new ParsedConfigs();
        }
      }

      return configs;
    }

    private void HandleGroups(string groupName, string configName, object? value, Dictionary<string, CustomSyncedValueBase> customValueMap, string typeName, ParsedConfigs configs, Dictionary<string, OwnConfigEntryBase> configMap)
    {
      if (groupName == "Internal")
      {
        HandleInternalGroup(configName, value, customValueMap, typeName, configs);
      }
      else if (configMap.TryGetValue(groupName + "_" + configName, out var config))
      {
        HandleExternalGroup(config, typeName, configs, value, configName, groupName);
      }
      else
      {
        Log.Warning(_loggerInstance, $"Received unknown config entry {configName} in section {groupName} for mod {DisplayName ?? Name}. This may happen if client and server versions of the mod do not match.");
      }
    }

    private void HandleInternalGroup(string configName, object? value, Dictionary<string, CustomSyncedValueBase> customValueMap, string typeName, ParsedConfigs configs)
    {
      switch (configName)
      {
        // ReSharper disable once StringLiteralTypo
        case "serverversion":
        {
          if (value?.ToString() != CurrentVersion)
          {
            Log.Warning(_loggerInstance, $"Received server version is not equal: server version = {value?.ToString() ?? "null"}; local version = {CurrentVersion ?? "unknown"}");
          }

          break;
        }

        // ReSharper disable once StringLiteralTypo
        case "lockexempt":
        {
          if (value is bool exempt)
          {
            _lockExempt = exempt;
          }

          break;
        }

        default:
        {
          if (customValueMap.TryGetValue(configName, out var config))
          {
            if ((typeName == "" && (!config.Type.IsValueType || Nullable.GetUnderlyingType(config.Type) != null)) || GetZPackageTypeString(config.Type) == typeName)
            {
              configs.customValues[config] = value;
            }
            else
            {
              Log.Warning(_loggerInstance, $"Got unexpected type {typeName} for internal value {configName} for mod {DisplayName ?? Name}, expecting {config.Type.AssemblyQualifiedName}");
            }
          }

          break;
        }
      }
    }

    private void HandleExternalGroup(OwnConfigEntryBase config, string typeName, ParsedConfigs configs, object? value, string configName, string groupName)
    {
      var expectedType = ConfigType(config.BaseConfig);
      if ((typeName == "" && (!expectedType.IsValueType || Nullable.GetUnderlyingType(expectedType) != null)) || GetZPackageTypeString(expectedType) == typeName)
      {
        configs.configValues[config] = value;
      }
      else
      {
        Log.Warning(_loggerInstance, $"Got unexpected type {typeName} for {configName} in section {groupName} for mod {DisplayName ?? Name}, expecting {expectedType.AssemblyQualifiedName}");
      }
    }

    private static bool IsWritableConfig(OwnConfigEntryBase config)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (_configSyncs.FirstOrDefault(cs => cs._allConfigs.Contains(config)) is not { } configSync)
      {
        return true;
      }

      return configSync.IsSourceOfTruth
             || !config.SynchronizedConfig
             || config.LocalBaseValue == null
             || (!configSync.IsLocked
                 && (config != configSync._lockedConfig
                     || _lockExempt));
    }

    private void ServerLockedSettingChanged()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      foreach (var configEntryBase in _allConfigs)
      {
        ConfigAttribute<ConfigurationManagerAttributes>(configEntryBase.BaseConfig).ReadOnly = !IsWritableConfig(configEntryBase);
      }
    }

    private void ResetConfigsFromServer()
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      foreach (var config in _allConfigs.Where(config => config.LocalBaseValue != null))
      {
        config.BaseConfig.BoxedValue = config.LocalBaseValue;
        config.LocalBaseValue = null;
      }

      foreach (var config in _allCustomValues.Where(config => config.LocalBaseValue != null))
      {
        config.BoxedValue = config.LocalBaseValue;
        config.LocalBaseValue = null;
      }

      _lockedConfigChanged -= ServerLockedSettingChanged;
      IsSourceOfTruth = true;
      ServerLockedSettingChanged();
    }

    private static long packageCounter;

    private IEnumerator<bool> DistributeConfigToPeers(ZNetPeer peer, ZPackage package)
    {
      if (ZRoutedRpc.instance is not { } rpc)
      {
        yield break;
      }

      const int packageSliceSize = 250000;
      const int maximumSendQueueSize = 20000;

      IEnumerable<bool> waitForQueue()
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        var timeout = Time.time + 30;
        while (peer.m_socket.GetSendQueueSize() > maximumSendQueueSize)
        {
          if (Time.time > timeout)
          {
            Log.Debug(_loggerInstance, $"Disconnecting {peer.m_uid} after 30 seconds config sending timeout");
            peer.m_rpc.Invoke("Error", ZNet.ConnectionStatus.ErrorConnectFailed);
            ZNet.instance.Disconnect(peer);
            yield break;
          }

          yield return false;
        }
      }

      void SendPackage(ZPackage pkg)
      {
        Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
        var method = Name + " ConfigSync";
        if (_isServer)
        {
          peer.m_rpc.Invoke(method, pkg);
        }
        else
        {
          rpc.InvokeRoutedRPC(peer.m_server ? 0 : peer.m_uid, method, pkg);
        }
      }

      if (package.GetArray() is { LongLength: > packageSliceSize } data)
      {
        var fragments = (int)(1 + (data.LongLength - 1) / packageSliceSize);
        var packageIdentifier = ++packageCounter;
        for (var fragment = 0; fragment < fragments; ++fragment)
        {
          foreach (var wait in waitForQueue())
          {
            yield return wait;
          }

          if (!peer.m_socket.IsConnected())
          {
            yield break;
          }

          ZPackage fragmentedPackage = new();
          fragmentedPackage.Write(FRAGMENTED_CONFIG);
          fragmentedPackage.Write(packageIdentifier);
          fragmentedPackage.Write(fragment);
          fragmentedPackage.Write(fragments);
          fragmentedPackage.Write(data.Skip(packageSliceSize * fragment).Take(packageSliceSize).ToArray());
          SendPackage(fragmentedPackage);

          if (fragment != fragments - 1)
          {
            yield return true;
          }
        }
      }
      else
      {
        foreach (var wait in waitForQueue())
        {
          yield return wait;
        }

        SendPackage(package);
      }
    }

    private IEnumerator SendZPackage(long target, ZPackage package)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (!ZNet.instance)
      {
        return Enumerable.Empty<object>().GetEnumerator();
      }

      var peers = (List<ZNetPeer>)AccessTools.DeclaredField(typeof(ZRoutedRpc), nameof(ZRoutedRpc.m_peers)).GetValue(ZRoutedRpc.instance);
      if (target != ZRoutedRpc.Everybody)
      {
        peers = peers.Where(p => p.m_uid == target).ToList();
      }

      return SendZPackage(peers, package);
    }

    private IEnumerator SendZPackage(List<ZNetPeer> peers, ZPackage package)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (!ZNet.instance)
      {
        yield break;
      }

      const int compressMinSize = 10000;

      if (package.GetArray() is { LongLength: > compressMinSize } rawData)
      {
        ZPackage compressedPackage = new();
        compressedPackage.Write(COMPRESSED_CONFIG);
        MemoryStream output = new();
        using (DeflateStream deflateStream = new(output, CompressionLevel.Optimal))
        {
          deflateStream.Write(rawData, 0, rawData.Length);
        }

        compressedPackage.Write(output.ToArray());
        package = compressedPackage;
      }

      var writers = peers.Where(peer => peer.IsReady()).Select(p => DistributeConfigToPeers(p, package)).ToList();
      writers.RemoveAll(writer => !writer.MoveNext());
      while (writers.Count > 0)
      {
        yield return null;
        writers.RemoveAll(writer => !writer.MoveNext());
      }
    }

    private class PackageEntry
    {
      public string section = null!;
      public string key = null!;
      public Type type = null!;
      public object? value;
    }

    private void Broadcast(long target, params ConfigEntryBase[] configs)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (IsLocked && !_isServer) return;
      var package = ConfigsToPackage(configs);
      ZNet.instance?.StartCoroutine(SendZPackage(target, package));
    }

    private void Broadcast(long target, params CustomSyncedValueBase[] customValues)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (IsLocked && !_isServer) return;
      var package = ConfigsToPackage(customValues: customValues);
      ZNet.instance?.StartCoroutine(SendZPackage(target, package));
    }

    private static OwnConfigEntryBase? ConfigData(ConfigEntryBase config)
    {
      return config.Description.Tags?.OfType<OwnConfigEntryBase>().SingleOrDefault();
    }

    public static SyncedConfigEntry<T>? ConfigData<T>(ConfigEntry<T> config)
    {
      return config.Description.Tags?.OfType<SyncedConfigEntry<T>>().SingleOrDefault();
    }

    private static T ConfigAttribute<T>(ConfigEntryBase config)
    {
      return config.Description.Tags.OfType<T>().First();
    }

    private static Type ConfigType(ConfigEntryBase config) => ConfigType(config.SettingType);

    private static Type ConfigType(Type type) => type.IsEnum ? Enum.GetUnderlyingType(type) : type;

    private static ZPackage ConfigsToPackage(IEnumerable<ConfigEntryBase>? configs = null, IEnumerable<CustomSyncedValueBase>? customValues = null, IEnumerable<PackageEntry>? packageEntries = null, bool partial = true)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      var configList = configs?.Where(config => ConfigData(config)!.SynchronizedConfig).ToList() ?? new List<ConfigEntryBase>();
      var customValueList = customValues?.ToList() ?? new List<CustomSyncedValueBase>();
      ZPackage package = new();
      package.Write(partial ? PARTIAL_CONFIGS : (byte)0);
      package.Write(configList.Count + customValueList.Count + (packageEntries?.Count() ?? 0));
      foreach (var packageEntry in packageEntries ?? Array.Empty<PackageEntry>())
      {
        AddEntryToPackage(package, packageEntry);
      }

      foreach (var customValue in customValueList)
      {
        AddEntryToPackage(package, new PackageEntry { section = "Internal", key = customValue.Identifier, type = customValue.Type, value = customValue.BoxedValue });
      }

      foreach (var config in configList)
      {
        AddEntryToPackage(package, new PackageEntry { section = config.Definition.Section, key = config.Definition.Key, type = ConfigType(config), value = config.BoxedValue });
      }

      return package;
    }

    private static void AddEntryToPackage(ZPackage package, PackageEntry entry)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      package.Write(entry.section);
      package.Write(entry.key);
      package.Write(entry.value == null ? "" : GetZPackageTypeString(entry.type));
      AddValueToZPackage(package, entry.value);
    }

    private static string GetZPackageTypeString(Type type) => type.AssemblyQualifiedName!;

    private static void AddValueToZPackage(ZPackage package, object? value)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      var type = value?.GetType();
      switch (value)
      {
        case Enum:
          value = ((IConvertible)value).ToType(Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
          break;

        case ICollection collection:
          package.Write(collection.Count);
          foreach (var item in collection)
          {
            AddValueToZPackage(package, item);
          }

          return;

        default:
          if (type is { IsValueType: true, IsPrimitive: false })
          {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            package.Write(fields.Length);
            foreach (var field in fields)
            {
              package.Write(GetZPackageTypeString(field.FieldType));
              AddValueToZPackage(package, field.GetValue(value));
            }

            return;
          }

          break;
      }

      ZRpc.Serialize(new[] { value }, ref package);
    }

    private static object ReadValueWithTypeFromZPackage(ZPackage package, Type type)
    {
      Log.Trace(_loggerInstance, $"{_namespace}.{MethodBase.GetCurrentMethod()?.DeclaringType?.Name}.{MethodBase.GetCurrentMethod()?.Name}");
      if (type is { IsValueType: true, IsPrimitive: false, IsEnum: false })
      {
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var fieldCount = package.ReadInt();
        if (fieldCount != fields.Length)
        {
          throw new InvalidDeserializationTypeException { received = $"(field count: {fieldCount})", expected = $"(field count: {fields.Length})" };
        }

        var value = FormatterServices.GetUninitializedObject(type);
        foreach (var field in fields)
        {
          var typeName = package.ReadString();
          if (typeName != GetZPackageTypeString(field.FieldType))
          {
            throw new InvalidDeserializationTypeException { received = typeName, expected = GetZPackageTypeString(field.FieldType), field = field.Name };
          }

          field.SetValue(value, ReadValueWithTypeFromZPackage(package, field.FieldType));
        }

        return value;
      }

      if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
      {
        var entriesCount = package.ReadInt();
        var dict = (IDictionary)Activator.CreateInstance(type);
        var kvType = typeof(KeyValuePair<,>).MakeGenericType(type.GenericTypeArguments);
        var keyField = kvType.GetField("key", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var valueField = kvType.GetField("value", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < entriesCount; ++i)
        {
          var kv = ReadValueWithTypeFromZPackage(package, kvType);
          dict.Add(keyField.GetValue(kv), valueField.GetValue(kv));
        }

        return dict;
      }

      if (type != typeof(List<string>) && type.IsGenericType && typeof(ICollection<>).MakeGenericType(type.GenericTypeArguments[0]) is { } collectionType && collectionType.IsAssignableFrom(type.GetGenericTypeDefinition()))
      {
        var entriesCount = package.ReadInt();
        var list = Activator.CreateInstance(type);
        var adder = collectionType.GetMethod("Add")!;
        for (var i = 0; i < entriesCount; ++i)
        {
          adder.Invoke(list, new[] { ReadValueWithTypeFromZPackage(package, type.GenericTypeArguments[0]) });
        }

        return list;
      }

      var param = (ParameterInfo)FormatterServices.GetUninitializedObject(typeof(ParameterInfo));
      AccessTools.DeclaredField(typeof(ParameterInfo), "ClassImpl").SetValue(param, type);
      List<object> data = new();
      ZRpc.Deserialize(new[] { null, param }, package, ref data);
      return data.First();
    }

    private class InvalidDeserializationTypeException : Exception
    {
      public string expected = null!;
      public string received = null!;
      public string field = "";
    }
  }
}
