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

namespace Test.WinFormServer
{
    public partial class ServerForm : Form
    {
        private string _ClientIpPort = null;
        private WatsonTcpServer _Server = null;
        
        public ServerForm()
        {
            InitializeComponent();

            label1.Text = "";

            _Server = new WatsonTcpServer("127.0.0.1", 9000);
            // _Server.MaxConnections = 1;
            _Server.MessageReceived += OnMessageReceived;
            _Server.ClientConnected += OnClientConnected;
            _Server.ClientDisconnected += OnClientDisconnected;
            _Server.Logger = Logger;
            _Server.Start();

            label1.Text += Environment.NewLine + "Server started.";
        }
         
        private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            label1.Text += Environment.NewLine + "Client " + e.IpPort + " disconnected: " + e.Reason.ToString();
            _ClientIpPort = string.Empty;
        }

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            label1.Text += Environment.NewLine + "Client " + e.IpPort + " connected";
            _ClientIpPort = e.IpPort;
        }

        private void OnMessageReceived(object sender, MessageReceivedFromClientEventArgs e)
        {
            label1.Text += Environment.NewLine + "Client " + e.IpPort + ": " + Encoding.UTF8.GetString(e.Data);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(_ClientIpPort))
            {
                _Server.Send(_ClientIpPort, "Hello world!");
                label1.Text += Environment.NewLine + "Sent 'Hello world!' to client " + _ClientIpPort;
            }
            else
            {
                label1.Text += Environment.NewLine + "No client connected";
            }
        }

        private void Logger(string msg)
        {
            label1.Text += Environment.NewLine + msg;
        }
    }
}