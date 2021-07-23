namespace Digitalroot.Valheim.Common
{
  internal static class GuiUtils
  {
    internal static void PrintToCenterOfScreen(string msg)
    {
      Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg);
    }

    internal static void PrintToTopLeftOfScreen(string msg)
    {
      Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, msg);
    }
  }
}
