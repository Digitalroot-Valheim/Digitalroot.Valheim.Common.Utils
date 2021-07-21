using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Digitalroot.Valheim.Common
{
  public static class Utils
  {
    public static string Namespace = nameof(Common);
    public static readonly bool IsRunningFromNUnit =  AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName.ToLowerInvariant().StartsWith("nunit.framework"));

    // Source: EpicLoot
    public static bool IsObjectDBReady()
    {
      Log.Trace($"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Hack, just making sure the built-in items and prefabs have loaded
      return (ObjectDB.instance != null && ObjectDB.instance.m_items.Count != 0 && ObjectDB.instance.GetItemPrefab("Amber") != null) || IsRunningFromNUnit;
    }

    public static bool IsZNetSceneReady()
    {
      Log.Trace($"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      return ZNetScene.instance != null && ZNetScene.instance?.m_prefabs != null && ZNetScene.instance?.m_prefabs?.Count > 0;
    }

    public static bool IsZNetReady()
    {
      Log.Trace($"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Log.Trace($"ZNet.instance != null : {ZNet.instance != null}");
      return ZNet.instance != null;
    }

    public static bool IsPlayerReady()
    {
      Log.Trace($"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Log.Trace($"Player.m_localPlayer == null : {Player.m_localPlayer == null}");
      return Player.m_localPlayer != null;
    }

    public static bool IsServer => ZNet.instance.IsServer();
    public static bool IsDedicated => ZNet.instance.IsDedicated();

    public static string Localize(string value)
    {
      return Localization.instance.Localize(value);
    }

    public static DirectoryInfo AssemblyDirectory
    {
      get
      {
        string codeBase = Assembly.GetExecutingAssembly().CodeBase;
        UriBuilder uri = new UriBuilder(codeBase);
        var fileInfo = new FileInfo(Uri.UnescapeDataString(uri.Path));
        return fileInfo.Directory;
      }
    }

    public static void ToggleTrace(bool value)
    {
      switch (value)
      {
        case true:
          Log.EnableTrace();
          break;
        default:
          Log.DisableTrace();
          break;
      }
    }

    internal static IEnumerable<string> AllNames(Type type)
    {
      var f = type.GetFields().Where(f1 => f1.FieldType == typeof(string));
      foreach (var fieldInfo in f)
      {
        yield return fieldInfo.GetValue(null).ToString();
      }
    }
  }
}
