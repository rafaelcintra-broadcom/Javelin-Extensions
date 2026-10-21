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

                var database = MongoConnectionHelper.GetDatabase(connStr, dbName);
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
