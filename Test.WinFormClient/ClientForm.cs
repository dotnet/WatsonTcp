using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WatsonTcp;

namespace Test.WinFormClient
{
    public partial class ClientForm : Form
    {
        private WatsonTcpClient _Client = null;

        delegate void _LogDelegate(string msg);
        
        public ClientForm()
        {
            InitializeComponent();
            label1.Text = "";
            
            _Client = new WatsonTcpClient("127.0.0.1", 9000);
            _Client.Events.ServerConnected += ServerConnected;
            _Client.Events.ServerDisconnected += ServerDisconnected;
            _Client.Events.AuthenticationFailure += OnAuthenticationFailure;
            _Client.Events.MessageReceived += MessageReceived;
            _Client.Settings.Logger = Logger;
        }
         
        private void button1_Click(object sender, EventArgs e)
        {
            _Client.Connect();
        }
         
        private void OnAuthenticationFailure(object sender, EventArgs e)
        {
            Logger("Authentication failure.");
        }

        private void ServerConnected(object sender, ConnectionEventArgs args)
        {
            Logger(args.IpPort + " connected");
        }

        private void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Logger(args.IpPort + " disconnected: " + args.Reason.ToString());
        }

        private void button2_Click(object sender, EventArgs e)
        {
            _Client.Send("Hello world!");
            Logger("Sent message 'Hello world!'");
        }

        private void Logger(string msg)
        {
            // If this is called by another thread we have to use Invoke           
            if (this.InvokeRequired)
                this.Invoke(new _LogDelegate(Logger), new object[] { msg });
            else
                label1.Text += Environment.NewLine + msg;
                
        }

        private void ClientForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _Client.Dispose();
        }

        private void MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Logger("Message from " + e.IpPort + ": " + Encoding.UTF8.GetString(e.Data));
        }
    }
}
