namespace Digitalroot.Valheim.Common.Config.Providers.ServerSync
{
  public class ServerSyncProxyCustomSyncedValue<T> : AbstractProxyCustomSyncedValue<T>
  {
    /// <inheritdoc />
    public ServerSyncProxyCustomSyncedValue(CustomSyncedValue<T> customSyncedValue)
      : base(customSyncedValue) { }
  }
}
