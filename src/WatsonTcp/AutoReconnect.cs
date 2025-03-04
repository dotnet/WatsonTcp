// WatsonTCP Auto Reconnect feature.
// By Shayan Firoozi , Bandar Abbas , Iran
// https://github.com/ShayanFiroozi
// Shayan.Firoozi@gmail.com

namespace WatsonTcp
{
    using System;
    using System.Security.Cryptography.X509Certificates;
    using System.Timers;

    public partial class WatsonTcpClient : IDisposable
    {

        private double _AutoReconnectIntervalMs { get; set; } = 0;
        private int _AutoReconnectMaxAttempts { get; set; } = -1;
        private int _AutoReconnectAttempts = 0;
        private Timer _AutoReconnectTimer = null;

        private void StartAutoReconnect()
        {
            _AutoReconnectTimer = new Timer(_AutoReconnectIntervalMs);
            _AutoReconnectTimer.Elapsed += AutoReconnecTimer_Elapsed;
            _AutoReconnectTimer.Enabled = true;
            _AutoReconnectTimer.AutoReset = true;
            _AutoReconnectTimer.Start();
        }

        private void StopAutoReconnect()
        {
            try
            {
                if (_AutoReconnectTimer != null)
                {
                    _AutoReconnectTimer.Enabled = false;
                    _AutoReconnectTimer.Stop();
                    _AutoReconnectAttempts = 0;
                }
            }
            catch { }
        }

        private void AutoReconnecTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (_Client is null) return;

                if (!Connected)
                {
                    if (_AutoReconnectMaxAttempts != -1 && _AutoReconnectAttempts > _AutoReconnectMaxAttempts)
                    {
                        StopAutoReconnect();
                        return;
                    }
                    else
                    {
                        Connect();

                        _AutoReconnectAttempts++;
                    }
                }
                else
                {
                    _AutoReconnectAttempts = 0;
                }
            }

            catch { }
        }
    }
}
