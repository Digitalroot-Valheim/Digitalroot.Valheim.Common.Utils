using BepInEx.Configuration;
using Digitalroot.Valheim.Common.Config.Providers;

namespace Digitalroot.Valheim.Common.Config
{
  public abstract class AbstractConfigProvider
  {
    public abstract ConfigEntry<T> AddConfigEntry<T>(ConfigEntry<T> configEntry);
    public abstract ConfigEntry<T> AddConfigEntry<T>(string group, string name, T value, ConfigDescription description);
    public virtual ConfigEntry<T> AddConfigEntry<T>(string group, string name, T value, string description) => AddConfigEntry(group, name, value, new ConfigDescription(description));
    public abstract AbstractProxyCustomSyncedValue<T> AddCustomSyncedValue<T>(string identifier, T value = default);
  }
}
