using System;
using System.Net.Sockets;
using Perpetuum.Log;

namespace Perpetuum.Network
{
    public static class SocketExtensions
    {
        [UsedImplicitly]
        public static void SetKeepAlive(this Socket socket, bool state, TimeSpan time, TimeSpan interval)
        {
            socket.SetKeepAlive(state, (uint)time.TotalMilliseconds, (uint)interval.TotalMilliseconds);
        }
        
        public static void SetKeepAlive(this Socket socket, bool state, uint time, uint interval)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, state);
                if (state)
                {
                    int keepAliveTimeSeconds = checked((int) Math.Max(1, (time + 999L) / 1000L));
                    int keepAliveIntervalSeconds = checked((int) Math.Max(1, (interval + 999L) / 1000L));

                    // Linux exposes these options as signed 16-bit values and
                    // rejects larger values with EINVAL. The server's historical
                    // one-day idle timeout exceeds that limit, so use the closest
                    // value Linux accepts while preserving the requested value on
                    // platforms that support it.
                    if (OperatingSystem.IsLinux())
                    {
                        keepAliveTimeSeconds = Math.Min(keepAliveTimeSeconds, short.MaxValue);
                        keepAliveIntervalSeconds = Math.Min(keepAliveIntervalSeconds, short.MaxValue);
                    }

                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, keepAliveTimeSeconds);
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, keepAliveIntervalSeconds);
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
        }

    }
}
