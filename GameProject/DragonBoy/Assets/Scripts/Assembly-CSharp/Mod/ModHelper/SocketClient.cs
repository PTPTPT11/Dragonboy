namespace Mod.ModHelper
{
    /// <summary>
    /// NRO PRIVATE hardened build: external QLTK socket bridge is disabled.
    /// No username/password/key events are received from another process.
    /// </summary>
    public class SocketClient : ThreadAction<SocketClient>
    {
        public int port = -1;
        public bool isConnected = false;

        public void initSender()
        {
            isConnected = false;
        }

        public void sendMessage(object obj)
        {
        }

        protected override void action()
        {
        }

        public void close()
        {
            isConnected = false;
        }
    }
}
