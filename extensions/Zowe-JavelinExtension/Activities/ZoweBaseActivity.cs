using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    public abstract class ZoweBaseActivity : JavelinNativeActivity
    {
        [Category("1. Zowe Profile & Config")]
        [DisplayName("z/OSMF Profile Name")]
        [Description("Name of pre-configured Zowe CLI z/OSMF profile (e.g. 'zosmf_dev'). If empty, uses default active profile or direct credentials.")]
        public InArgument<string> ProfileName { get; set; }

        [Category("1. Zowe Profile & Config")]
        [DisplayName("Base Profile Name")]
        [Description("Name of Zowe base profile (e.g. 'base_apiml') when using Zowe API Mediation Layer.")]
        public InArgument<string> BaseProfileName { get; set; }

        [Category("2. Direct Connection")]
        [DisplayName("Host / URL")]
        [Description("Mainframe host or IP (e.g. zosmf.company.com)")]
        public InArgument<string> Host { get; set; }

        [Category("2. Direct Connection")]
        [DisplayName("Port")]
        [Description("z/OSMF HTTPS port (e.g. 10443, 443)")]
        public InArgument<int?> Port { get; set; }

        [Category("2. Direct Connection")]
        [DisplayName("Base Path")]
        [Description("Optional base path (e.g. zosmf/api/v1)")]
        public InArgument<string> BasePath { get; set; }

        [Category("3. Authentication")]
        [DisplayName("API Token (APIML / JWT / LTPA)")]
        [JavelinPassword]
        [Description("API Token or Bearer Token for Zowe API Mediation Layer (e.g. apimlAuthenticationToken). When provided, overrides username/password.")]
        public InArgument<string> ApiToken { get; set; }

        [Category("3. Authentication")]
        [DisplayName("Token Type")]
        [Description("Type of token: 'apimlAuthenticationToken', 'Bearer', 'jwtToken', 'LtpaToken2'. Default: 'apimlAuthenticationToken'.")]
        public InArgument<string> TokenType { get; set; }

        [Category("3. Authentication")]
        [DisplayName("User ID")]
        [Description("Mainframe user ID (RACF / ACF2 / Top Secret)")]
        public InArgument<string> Username { get; set; }

        [Category("3. Authentication")]
        [DisplayName("Password")]
        [JavelinPassword]
        [Description("Mainframe user password / passphrase")]
        public InArgument<string> Password { get; set; }

        [Category("3. Authentication")]
        [DisplayName("Client Certificate File Path")]
        [Description("Path to client certificate (PEM / CER / PFX) for PKI authentication")]
        public InArgument<string> CertFilePath { get; set; }

        [Category("3. Authentication")]
        [DisplayName("Client Certificate Key Path")]
        [Description("Path to client private key file")]
        public InArgument<string> CertKeyFilePath { get; set; }

        [Category("4. Environment & Options")]
        [DisplayName("Reject Unauthorized SSL (ru)")]
        [DefaultValue(false)]
        [Description("Set to false to accept self-signed certificates from z/OSMF")]
        public InArgument<bool> RejectUnauthorized { get; set; }

        [Category("4. Environment & Options")]
        [DisplayName("Custom Zowe CLI Path")]
        [Description("Full path to zowe executable if not in system PATH (e.g. C:\\Users\\user\\AppData\\Roaming\\npm\\zowe.cmd)")]
        public InArgument<string> CustomZowePath { get; set; }

        [Category("4. Environment & Options")]
        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(120)]
        public InArgument<int> TimeoutSeconds { get; set; }

        protected ZoweProfileOptions BuildProfileOptions(NativeActivityContext context)
        {
            return new ZoweProfileOptions
            {
                ZosmfProfile = ProfileName.Get(context),
                BaseProfile = BaseProfileName.Get(context),
                Host = Host.Get(context),
                Port = Port.Get(context),
                BasePath = BasePath.Get(context),
                TokenValue = ApiToken.Get(context),
                TokenType = TokenType.Get(context),
                User = Username.Get(context),
                Password = Password.Get(context),
                CertPath = CertFilePath.Get(context),
                CertKeyPath = CertKeyFilePath.Get(context),
                RejectUnauthorized = RejectUnauthorized.Get(context)
            };
        }
    }
}
