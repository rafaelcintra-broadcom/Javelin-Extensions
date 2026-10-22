using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase SSH Tunnel")]
    [Description("Establishes a background SSH local port forwarding tunnel (e.g. localhost:8080 -> hbase-cluster:8080) for Javelin REST activities")]
    public class HBaseSshTunnelActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("SSH Host (Bastion/Edge)")]
        public InArgument<string> Host { get; set; }

        [DisplayName("SSH Port")]
        [DefaultValue(22)]
        public InArgument<int> Port { get; set; }

        [RequiredArgument]
        [DisplayName("SSH Username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("SSH Password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Private Key Path")]
        public InArgument<string> PrivateKeyPath { get; set; }

        [DisplayName("Private Key Passphrase")]
        [JavelinPassword]
        public InArgument<string> Passphrase { get; set; }

        [DisplayName("Local Bound Port")]
        [Description("Local port on Windows machine running Javelin (e.g. 18080)")]
        [DefaultValue(18080)]
        public InArgument<uint> LocalPort { get; set; }

        [DisplayName("Remote Target Host")]
        [Description("HBase REST host from bastion's network perspective (e.g. 127.0.0.1 or hbase-master01)")]
        [DefaultValue("127.0.0.1")]
        public InArgument<string> RemoteHost { get; set; }

        [DisplayName("Remote Target Port")]
        [Description("HBase REST port on remote cluster (e.g. 8080)")]
        [DefaultValue(8080)]
        public InArgument<uint> RemotePort { get; set; }

        // --- Outputs ---

        [DisplayName("Tunnel URL")]
        [Description("Convenience URL to pass directly to HBase REST activities (e.g. http://localhost:18080)")]
        public OutArgument<string> TunnelUrl { get; set; }

        [DisplayName("Is Connected")]
        public OutArgument<bool> IsConnected { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string host = Host.Get(context);
                int port = Port.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string keyPath = PrivateKeyPath.Get(context);
                string passphrase = Passphrase.Get(context);
                uint localPort = LocalPort.Get(context);
                string remoteHost = RemoteHost.Get(context);
                uint remotePort = RemotePort.Get(context);

                if (localPort == 0) localPort = 18080;
                if (string.IsNullOrWhiteSpace(remoteHost)) remoteHost = "127.0.0.1";
                if (remotePort == 0) remotePort = 8080;

                var client = HBaseSshHelper.GetOrCreateSshClient(host, port, user, pass, keyPath, passphrase, 60);

                var forwardedPort = new Renci.SshNet.ForwardedPortLocal("127.0.0.1", localPort, remoteHost, remotePort);
                bool alreadyAdded = false;
                foreach (var p in client.ForwardedPorts)
                {
                    if (p is Renci.SshNet.ForwardedPortLocal fpl && fpl.Port == localPort)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                {
                    client.AddForwardedPort(forwardedPort);
                    forwardedPort.Start();
                }

                string tunnelUrl = $"http://127.0.0.1:{localPort}";
                TunnelUrl.Set(context, tunnelUrl);
                IsConnected.Set(context, forwardedPort.IsStarted);

                GlobalSettings.MessageLogger.LogMessage("HBase SSH Tunnel active: {0} -> {1}:{2} (via {3})",
                    tunnelUrl, remoteHost, remotePort, host);
            }
            catch (Exception ex)
            {
                var bex = new BusinessException(ex.Message, DisplayName, ex.InnerException);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
