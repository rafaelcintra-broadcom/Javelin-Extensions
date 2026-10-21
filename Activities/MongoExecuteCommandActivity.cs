using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Execute Command")]
    [Description("Run an administrative or arbitrary database command (e.g. ping, drop, stats)")]
    public class MongoExecuteCommandActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("Connection String")]
        public InArgument<string> ConnectionString { get; set; }

        [RequiredArgument]
        [DisplayName("Database Name")]
        public InArgument<string> DatabaseName { get; set; }

        [RequiredArgument]
        [DisplayName("Command (JSON)")]
        [Description("Command document, e.g. {\"ping\": 1} or {\"dropDatabase\": 1}")]
        public InArgument<string> CommandJson { get; set; }

        [DisplayName("TDM Portal URL")]
        [Description("URL do TDM Portal (ex: https://tdm-server:8443). Se em branco, busca automaticamente no application.properties local.")]
        public InArgument<string> TdmPortalUrl { get; set; }

        [DisplayName("TDM Username")]
        [Description("Usuário do TDM Portal (opcional)")]
        public InArgument<string> TdmUsername { get; set; }

        [DisplayName("TDM Password")]
        [Description("Senha do usuário TDM (opcional)")]
        public InArgument<string> TdmPassword { get; set; }

        [DisplayName("TDM Auth Token (JWT)")]
        [Description("Token JWT pré-gerado do TDM Portal (opcional)")]
        public InArgument<string> TdmAuthToken { get; set; }

        [DisplayName("Enable Debug Logs")]
        [Description("Se habilitado, imprime no log do Javelin o detalhe de todas as chamadas HTTP para o TDM Portal")]
        [DefaultValue(false)]
        public InArgument<bool> EnableDebugLogs { get; set; }

        [DisplayName("Response (JSON)")]
        public OutArgument<string> ResponseJson { get; set; }

        [DisplayName("Is OK")]
        public OutArgument<bool> IsOk { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string commandStr = CommandJson.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);

                var database = MongoConnectionHelper.GetDatabase(
                    connStr, dbName, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);
                var commandDoc = BsonSerializer.Deserialize<BsonDocument>(commandStr);

                var command = new BsonDocumentCommand<BsonDocument>(commandDoc);
                var result = database.RunCommand(command);

                string resultJson = result.ToJson();
                ResponseJson.Set(context, resultJson);

                bool ok = result.Contains("ok") && result["ok"].ToDouble() == 1.0;
                IsOk.Set(context, ok);

                GlobalSettings.MessageLogger.LogMessage("MongoDB Command executed. Result OK: {0}", ok);
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
