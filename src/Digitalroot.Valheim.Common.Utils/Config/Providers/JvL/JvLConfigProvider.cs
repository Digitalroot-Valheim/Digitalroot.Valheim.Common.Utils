using BepInEx.Configuration;

namespace Digitalroot.Valheim.Common.Config.Providers.JvL
{
    public class JvLAbstractConfigProvider : AbstractConfigProvider
    {
      #region Overrides of AbstractConfigProvider

      /// <inheritdoc />
      public override ConfigEntry<T> AddConfigEntry<T>(ConfigEntry<T> configEntry)
      {
        throw new System.NotImplementedException();
      }

      /// <inheritdoc />
      public override ConfigEntry<T> AddConfigEntry<T>(string group, string name, T value, ConfigDescription description)
      {
        throw new System.NotImplementedException();
      }

      /// <inheritdoc />
      public override AbstractProxyCustomSyncedValue<T> AddCustomSyncedValue<T>(string identifier, T value = default)
      {
        throw new System.NotImplementedException();
      }

      #endregion
    }
}
