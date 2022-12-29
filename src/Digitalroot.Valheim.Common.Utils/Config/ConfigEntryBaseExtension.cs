using BepInEx.Configuration;
using System.Linq;

namespace Digitalroot.Valheim.Common.Config
{
  /// <summary>
  ///     Extends <see cref="ConfigEntryBase"/> with convenience functions.
  /// </summary>
  public static class ConfigEntryBaseExtension
  {
    /// <summary>
    ///     Check, if this config entry is "visible"
    /// </summary>
    /// <param name="configurationEntry"></param>
    /// <returns></returns>
    public static bool IsVisible(this ConfigEntryBase configurationEntry)
    {
      ConfigurationManagerAttributes attributes = new ConfigurationManagerAttributes();
      attributes.SetFromAttributes(configurationEntry.Description?.Tags);
      return attributes.Browsable != false;
    }

    /// <summary>
    ///     Check, if this config entry is "syncable"
    /// </summary>
    /// <param name="configurationEntry"></param>
    /// <returns></returns>
    public static bool IsSyncable(this ConfigEntryBase configurationEntry)
    {
      if (configurationEntry.Description.Tags.FirstOrDefault(x => x is ConfigurationManagerAttributes) is ConfigurationManagerAttributes cma)
      {
        return cma.IsAdminOnly;
      }

      return false;
    }

    /// <summary>
    ///     Get the local value of an admin config
    /// </summary>
    /// <param name="configurationEntry"></param>
    /// <returns></returns>
    internal static object GetLocalValue(this ConfigEntryBase configurationEntry)
    {
      if (configurationEntry.Description.Tags.FirstOrDefault(x => x is ConfigurationManagerAttributes) is ConfigurationManagerAttributes cma)
      {
        return cma.LocalValue;
      }

      return null;
    }

    /// <summary>
    ///     Set the local value of an admin config
    /// </summary>
    /// <param name="configurationEntry"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    internal static void SetLocalValue(this ConfigEntryBase configurationEntry, object value)
    {
      if (configurationEntry.Description.Tags.FirstOrDefault(x => x is ConfigurationManagerAttributes) is ConfigurationManagerAttributes cma)
      {
        cma.LocalValue = value;
      }
    }
  }
}
