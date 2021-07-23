namespace Digitalroot.Valheim.Common
{
  public static class GuiUtils
  {
    public static void PrintToCenterOfScreen(string msg)
    {
      Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg);
    }

    public static void PrintToTopLeftOfScreen(string msg)
    {
      Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, msg);
    }
  }
}
