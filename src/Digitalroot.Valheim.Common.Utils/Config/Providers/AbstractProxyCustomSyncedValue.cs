#nullable enable
using Digitalroot.Valheim.Common.Config.Providers.ServerSync;
using JetBrains.Annotations;
using System;

namespace Digitalroot.Valheim.Common.Config.Providers
{
  public abstract class AbstractProxyCustomSyncedValue<T>
  {
    // ReSharper disable once InconsistentNaming
    private CustomSyncedValue<T> _customSyncedValue { get; }

    protected AbstractProxyCustomSyncedValue(CustomSyncedValue<T> customSyncedValue)
    {
      _customSyncedValue = customSyncedValue;
      _customSyncedValue.ValueChanged += OnValueChanged;
    }

    [CanBeNull]
    public event Action? ValueChanged;

    private void OnValueChanged()
    {
      ValueChanged?.Invoke();
    }

    public T Value => _customSyncedValue.Value;
    public object? BoxedValue => _customSyncedValue.BoxedValue;
    public System.Type Type => _customSyncedValue.Type;
    public string Guid => _customSyncedValue.Identifier;
    public void SetValue(T newValue) => _customSyncedValue.AssignLocalValue(newValue);
  }
}
