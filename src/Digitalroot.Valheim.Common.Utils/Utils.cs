using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Digitalroot.Valheim.Common
{
  public static class Utils
  {
    private static readonly ITraceableLogging Logger = new StaticSourceLogger();

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

    public static bool IsDedicated => ZNet.instance.IsDedicated();

    public static bool IsServer => ZNet.instance.IsServer();

    // ReSharper disable once MemberCanBePrivate.Global
    public static bool IsRunningFromNUnit => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName.ToLowerInvariant().StartsWith("nunit.framework"));

    // ReSharper disable once MemberCanBePrivate.Global
    public static string Namespace => nameof(Common);

    public static IEnumerable<string> AllNames(Type type)
    {
      var f = type.GetFields().Where(f1 => f1.FieldType == typeof(string));
      foreach (var fieldInfo in f)
      {
        yield return fieldInfo.GetValue(null).ToString();
      }
    }

    public static T GetPrivateField<T>(object instance, string name)
    {
      FieldInfo var = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (var == null)
      {
        Log.Error(Logger,"Variable " + name + " does not exist on type: " + instance.GetType());
        return default(T);
      }

      return (T) var.GetValue(instance);
    }

    public static object InvokePrivate(object instance, string name, object[] args = null)
    {
      MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (method == null)
      {
        Type[] types = args == null ? new Type[0] : args.Select(arg => arg.GetType()).ToArray();
        method = instance.GetType().GetMethod(name, types);
      }

      if (method == null)
      {
        Log.Error(Logger, "Method " + name + " does not exist on type: " + instance.GetType());
        return null;
      }

      return method.Invoke(instance, args);
    }

    // Source: EpicLoot
    public static bool IsObjectDBReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Hack, just making sure the built-in items and prefabs have loaded
      return (ObjectDB.instance != null && ObjectDB.instance.m_items.Count != 0 && ObjectDB.instance.GetItemPrefab("Amber") != null) || IsRunningFromNUnit;
    }

    public static bool IsPlayerReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Log.Trace($"Player.m_localPlayer == null : {Player.m_localPlayer == null}");
      return Player.m_localPlayer != null;
    }

    public static bool IsZNetSceneReady()
    {
      Log.Trace(Logger,$"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      return ZNetScene.instance != null && ZNetScene.instance?.m_prefabs != null && ZNetScene.instance?.m_prefabs?.Count > 0;
    }

    public static bool IsZNetReady()
    {
      Log.Trace(Logger, $"{Namespace}.{MethodBase.GetCurrentMethod().DeclaringType?.Name}.{MethodBase.GetCurrentMethod().Name}");
      // Log.Trace($"ZNet.instance != null : {ZNet.instance != null}");
      return ZNet.instance != null;
    }

    public static string Localize(string value)
    {
      return Localization.instance.Localize(value);
    }

    public static void SetPrivateField(object instance, string name, object value)
    {
      FieldInfo var = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

      if (var == null)
      {
        Log.Error(Logger, "Variable " + name + " does not exist on type: " + instance.GetType());
        return;
      }

      var.SetValue(instance, value);
    }
  }
}
