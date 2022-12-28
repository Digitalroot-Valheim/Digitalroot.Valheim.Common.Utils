#nullable enable
using JetBrains.Annotations;

namespace Digitalroot.Valheim.Common.Config.Providers.ServerSync
{
  [PublicAPI]
  public sealed class CustomSyncedValue<T> : CustomSyncedValueBase
  {
    public T Value
    {
      get => (T)BoxedValue!;
      set => BoxedValue = value;
    }

    public CustomSyncedValue(ConfigSync configSync, string identifier, T value = default!)
      : base(configSync, identifier, typeof(T))
    {
      Value = value;
    }

    public void AssignLocalValue(T value)
    {
      if (localIsOwner)
      {
        Value = value;
      }
      else
      {
        LocalBaseValue = value;
      }
    }
  }
}
