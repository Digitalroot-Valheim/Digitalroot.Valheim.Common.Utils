#nullable enable
using BepInEx.Configuration;
using JetBrains.Annotations;

namespace Digitalroot.Valheim.Common.Config.Providers.ServerSync
{
  [PublicAPI]
  public abstract class OwnConfigEntryBase
  {
    public object? LocalBaseValue;
    public abstract ConfigEntryBase BaseConfig { get; }

    public bool SynchronizedConfig = true;
  }
}
