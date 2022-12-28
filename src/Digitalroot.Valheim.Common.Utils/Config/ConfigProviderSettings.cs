using BepInEx;
using JetBrains.Annotations;

namespace Digitalroot.Valheim.Common.Config
{
  [UsedImplicitly]
  public class ConfigProviderSettings
  {
    public string ModName { get; set; }
    public string ModGuid { get; set; }
    public string ModVersion { get; set; }
    public bool ModRequired { get; set; }
    public bool IsAdminOnly { get; set; }
    public BaseUnityPlugin Plugin { get; set; }
  }
}
