using Digitalroot.Valheim.Common.Config.Providers;
using Digitalroot.Valheim.Common.Config.Providers.JvL;
using System;

namespace Digitalroot.Valheim.Common.Config
{
  public static class ConfigProviderFactory
  {
    public static AbstractConfigProvider GetConfigProvider(ConfigProviderType configProviderType, ConfigProviderSettings configProviderSettings)
    {
      switch (configProviderType)
      {
        case ConfigProviderType.JvL:
          return new JvLAbstractConfigProvider();

        case ConfigProviderType.ServerSync:
          return new ServerSyncConfigProvider(configProviderSettings);

        default:
          throw new ArgumentOutOfRangeException(nameof(configProviderType), configProviderType, null);
      }
    }
  }
}
