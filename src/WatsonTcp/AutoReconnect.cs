
// WatsonTCP Auto Reconnect feature.
// By Shayan Firoozi , Bandar Abbas , Iran
//https://github.com/ShayanFiroozi
// Shayan.Firoozi@gmail.com


using System;
using System.Security.Cryptography.X509Certificates;
using System.Timers;

namespace WatsonTcp
{
    public partial class WatsonTcpClient : IDisposable
    {

        private double AutoReconnectInterval { get; set; } = 0;
        private int AutoReconnectMaxTry { get; set; } = -1;

        private int AutoReconnectTryCounter = 0;

        private Timer AutoReconnectTimer = null;



      



        private void StartAutoReconnect()
        {


            AutoReconnectTimer = new Timer(AutoReconnectInterval);
            AutoReconnectTimer.Elapsed += AutoReconnecTimer_Elapsed;
            AutoReconnectTimer.Enabled = true;
            AutoReconnectTimer.AutoReset = true;
            AutoReconnectTimer.Start();


        }

        private void StopAutoReconnect()
        {
            try
            {
                if (AutoReconnectTimer != null)
                {
                    AutoReconnectTimer.Enabled = false;
                    AutoReconnectTimer.Stop();
                    AutoReconnectTryCounter = 0;
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
                    if (AutoReconnectMaxTry != -1 && AutoReconnectTryCounter > AutoReconnectMaxTry)
                    {
                        StopAutoReconnect();
                        return;
                    }
                    else
                    {
                        Connect();

                        AutoReconnectTryCounter++;
                    }
                }
                else
                {
                    AutoReconnectTryCounter = 0;
                }
            }

            catch { }
        }



    }
}
