using BepInEx.Configuration;
using Digitalroot.Valheim.Common.Config.Providers.ServerSync;

namespace Digitalroot.Valheim.Common.Config.Providers
{
  public class ServerSyncConfigProvider : AbstractConfigProvider
  {
    private readonly ConfigSync _serverSyncConfigProvider;
    private readonly ConfigProviderSettings _configProviderSettings;

    public ServerSyncConfigProvider(ConfigProviderSettings configProviderSettings)
    {
      _configProviderSettings = configProviderSettings;
      _serverSyncConfigProvider = new ConfigSync(_configProviderSettings.ModGuid)
      {
        CurrentVersion = _configProviderSettings.ModVersion
        , DisplayName = _configProviderSettings.ModName
        , IsLocked = _configProviderSettings.IsAdminOnly
        , MinimumRequiredVersion = _configProviderSettings.ModVersion
        , ModRequired = _configProviderSettings.ModRequired
      };

      var lockingConfigEntry = SyncedConfig("Config Sync"
                                            , "Lock Config"
                                            , configProviderSettings.IsAdminOnly
                                            , new ConfigDescription("[Server Only] The configuration is locked and may not be changed by clients once it has been synced from the server. Only valid for server config, will have no effect on clients."
                                                                    , tags: new ConfigurationManagerAttributes
                                                                    {
                                                                      ReadOnly = true
                                                                      , Browsable = false
                                                                      , IsAdvanced = true,
                                                                    }
                                                                   ));
      _serverSyncConfigProvider.AddLockingConfigEntry(lockingConfigEntry);
    }

    private ConfigEntry<T> SyncedConfig<T>(string group, string configName, T value, string description, bool synchronizedSetting = true) => SyncedConfig(group, configName, value, new ConfigDescription(description), synchronizedSetting);

    private ConfigEntry<T> SyncedConfig<T>(string group, string configName, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
      var configEntry = _configProviderSettings.Plugin.Config.Bind(group, configName, value, description);

      var syncedConfigEntry = _serverSyncConfigProvider.AddConfigEntry(configEntry);
      syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

      return configEntry;
    }

    #region Overrides of AbstractConfigProvider

    /// <inheritdoc />
    public override ConfigEntry<T> AddConfigEntry<T>(string group, string configName, T value, string description) => SyncedConfig(group, configName, value, description);

    /// <inheritdoc />
    public override ConfigEntry<T> AddConfigEntry<T>(string group, string configName, T value, ConfigDescription description) => SyncedConfig(group, configName, value, description);

    /// <inheritdoc />
    public override ConfigEntry<T> AddConfigEntry<T>(ConfigEntry<T> configEntry)
    {
      var syncedConfigEntry = _serverSyncConfigProvider.AddConfigEntry(configEntry);
      syncedConfigEntry.SynchronizedConfig = _configProviderSettings.IsAdminOnly;
      return configEntry;
    }

    /// <inheritdoc />
    public override AbstractProxyCustomSyncedValue<T> AddCustomSyncedValue<T>(string identifier, T value = default)
    {
      var customSyncedValue = new CustomSyncedValue<T>(_serverSyncConfigProvider, identifier, value);
      return new ServerSyncProxyCustomSyncedValue<T>(customSyncedValue);
    }

    #endregion
  }
}
