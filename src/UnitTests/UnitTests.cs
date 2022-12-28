using Digitalroot.Valheim.Common.Config;
using NUnit.Framework;

namespace UnitTests
{
  public class Tests
  {
    [SetUp]
    public void Setup() { }

    [Test, Explicit]
    public void Test1()
    {
      var configProviderSettings = new ConfigProviderSettings
      {
        IsAdminOnly = true
        , ModGuid = "UnitTest"
        , ModName = "My Unit Test"
        , ModRequired = true
        , ModVersion = "1.0.0"
        , Plugin = null
      };

      var configProvider = ConfigProviderFactory.GetConfigProvider(ConfigProviderType.ServerSync, configProviderSettings);
      var configEntry = configProvider.AddConfigEntry("group", "name", true, "desc");
      var boxedValue = configEntry.BoxedValue;
      var value = configEntry.Value;

      var customSyncedValue = configProvider.AddCustomSyncedValue("unitTest", true);
      var boxeVal = customSyncedValue.BoxedValue;
      var val = customSyncedValue.Value;
      var type = customSyncedValue.Type;
      var guid = customSyncedValue.Guid;

      customSyncedValue.ValueChanged += CustomSyncedValue_ValueChanged;
    }

    private void CustomSyncedValue_ValueChanged()
    {
      throw new System.NotImplementedException();
    }
  }
}
