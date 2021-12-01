using BepInEx.Bootstrap;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Digitalroot.Valheim.Common
{
  public static class Utils
  {
    private static readonly ITraceableLogging Logger = GetLogger();

    private static ITraceableLogging GetLogger()
    {
#if DEBUG
      return new StaticSourceLogger(true);
#else
      return new StaticSourceLogger();
#endif
    }

    [UsedImplicitly]
    public static DirectoryInfo AssemblyDirectory
    {
      get
      {
        var codeBase = Assembly.GetExecutingAssembly().CodeBase;
        UriBuilder uri = new(codeBase);
        var fileInfo = new FileInfo(Uri.UnescapeDataString(uri.Path));
        return fileInfo.Directory;
      }
    }

    /// <summary>
    /// Does not work in Awake()
    /// </summary>
    [UsedImplicitly] public static bool IsDedicated => ZNet.instance.IsDedicated();

    /// <summary>
    /// Does not work in Awake()
    /// </summary>
    [UsedImplicitly] public static bool IsServer => ZNet.instance.IsServer();

    /// <summary>
    /// Works in Awake()
    /// </summary>
    [UsedImplicitly] public static bool IsHeadless() => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once StringLiteralTypo
    public static bool IsRunningFromNUnit => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName.ToLowerInvariant().StartsWith("nunit.framework"));

    // ReSharper disable once MemberCanBePrivate.Global
    public static string Namespace => $"Digitalroot.Valheim.{nameof(Common)}";

    [UsedImplicitly]
    public static List<T> AllOf<T>() => Enum.GetValues(typeof(T)).OfType<T>().ToList();

    [UsedImplicitly]
    public static IEnumerable<string> AllNames(Type type)
    {
      foreach (var fieldInfo in type.GetFields().Where(f1 => f1.FieldType == typeof(string)))
      {
        yield return fieldInfo.GetValue(null).ToString();
      }
    }

    [UsedImplicitly]
    public static bool DoesPluginExist(string pluginGuid) => Chainloader.PluginInfos.Any(keyValuePair => keyValuePair.Value.Metadata.GUID == pluginGuid);

    [UsedImplicitly]
    public static Vector3 GetGroundHeight(int x, int z) => GetGroundHeight(new Vector3Int(x, 500, z));

    [UsedImplicitly]
    public static Vector3 GetGroundHeight(float x, float z) => GetGroundHeight(new Vector3(x, 500, z));

    // ReSharper disable once MemberCanBePrivate.Global
    public static Vector3 GetGroundHeight(Vector3Int vector3) => new(vector3.x, ZoneSystem.instance.GetGroundHeight(vector3), vector3.z);

    // ReSharper disable once MemberCanBePrivate.Global
    public static Vector3 GetGroundHeight(Vector3 vector3) => new(vector3.x, ZoneSystem.instance.GetGroundHeight(vector3), vector3.z);

    [UsedImplicitly]
    public static GameObject GetItemPrefab(string itemName) => IsObjectDBReady() ? GetObjectDB().GetItemPrefab(itemName) : null;

    [UsedImplicitly]
    public static GameObject GetItemPrefab(int hash) => IsObjectDBReady() ? GetObjectDB().GetItemPrefab(hash) : null;

    [UsedImplicitly]
    public static Player GetLocalPlayer() => Player.m_localPlayer;

    [UsedImplicitly]
    public static Vector3 GetLocalPlayersPosition() => Player.m_localPlayer.transform.position;

    [UsedImplicitly]
    public static ObjectDB GetObjectDB() => ObjectDB.instance;

    [UsedImplicitly]
    public static string GetPluginPath(Type modPluginType) => Path.GetDirectoryName(modPluginType.Assembly.Location);

    [UsedImplicitly]
    public static GameObject GetPrefab(string itemName) => IsZNetSceneReady() ? ZNetScene.instance.GetPrefab(itemName) : null;
    
    [UsedImplicitly]
    public static GameObject GetPrefab(int hash) => IsZNetSceneReady() ? ZNetScene.instance.GetPrefab(hash) : null;

    [UsedImplicitly]
    public static T GetPrivateField<T>(object instance, string name)
    {
      var var = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (var != null) return (T)var.GetValue(instance);
      Log.Error(Logger,"Variable " + name + " does not exist on type: " + instance.GetType());
      return default;

    }

    [UsedImplicitly]
    public static object InvokePrivate(object instance, string name, object[] args = null)
    {
      var method = instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (method == null)
      {
        var types = args == null ? Type.EmptyTypes : args.Select(arg => arg.GetType()).ToArray();
        method = instance.GetType().GetMethod(name, types);
      }

      if (method == null)
      {
        Log.Error(Logger, "Method " + name + " does not exist on type: " + instance.GetType());
        return null;
      }

      return method.Invoke(instance, args);
    }

    [UsedImplicitly]
    public static bool IsGameInMainScene() => ZNetScene.instance != null;

    [UsedImplicitly]
    public static bool IsObjectDBReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      return (GetObjectDB() != null && GetObjectDB().m_items.Count != 0 && GetItemPrefab("Amber") != null) || IsRunningFromNUnit;
    }

    [UsedImplicitly]
    public static bool IsPlayerReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      return GetLocalPlayer() != null;
    }

    [UsedImplicitly]
    public static bool IsZNetSceneReady()
    {
      Log.Trace(Logger,$"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      return ZNetScene.instance != null && ZNetScene.instance?.m_prefabs is { Count: > 0 };
    }

    [UsedImplicitly]
    public static bool IsZNetReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Log.Trace($"ZNet.instance != null : {ZNet.instance != null}");
      return ZNet.instance != null;
    }

    [UsedImplicitly]
    public static string Localize(string value)
    {
      return Localization.instance.Localize(value);
    }

    [UsedImplicitly]
    public static Vector3 GetStartTemplesPosition()
    {
      if (ZoneSystem.instance.FindClosestLocation("StartTemple", Vector3.zero, out var locationInstance))
      {
        Log.Trace(Logger, $"[GetStartTemplesPosition] StartTemple at {locationInstance.m_position}");
        return locationInstance.m_position;
      }
      Log.Error(Logger, $"[GetStartTemplesPosition] Can't find StartTemple");

      return Vector3.zero;
    }

    [UsedImplicitly]
    public static void SetPrivateField(object instance, string name, object value)
    {
      var var = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (var == null)
      {
        Log.Error(Logger, "Variable " + name + " does not exist on type: " + instance.GetType());
        return;
      }

      var.SetValue(instance, value);
    }

    [UsedImplicitly]
    public static GameObject Spawn(string prefabName, GameObject prefab, Vector3 location, Transform parent)
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}({prefab?.name}, {location}, {parent.name})");
      if (prefab == null) return null;
      var instance = UnityEngine.Object.Instantiate(prefab, location, Quaternion.identity, parent);
      return instance;
    }

    [UsedImplicitly]
    public static GameObject Spawn(GameObject prefab, Vector3 location, Transform parent)
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}({prefab.name}, {location}, {parent.name})");
      var instance = UnityEngine.Object.Instantiate(prefab, location, Quaternion.identity, parent);
      return instance;
    }
  }
}
