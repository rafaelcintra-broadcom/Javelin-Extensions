using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Linq;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Aggregate")]
    [Description("Run a MongoDB Aggregation Pipeline")]
    public class MongoAggregateActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("Connection String")]
        public InArgument<string> ConnectionString { get; set; }

        [RequiredArgument]
        [DisplayName("Database Name")]
        public InArgument<string> DatabaseName { get; set; }

        [RequiredArgument]
        [DisplayName("Collection Name")]
        public InArgument<string> CollectionName { get; set; }

        [RequiredArgument]
        [DisplayName("Pipeline (JSON Array)")]
        [Description("Array of pipeline stages, e.g. [ { \"$match\": { \"status\": \"A\" } }, { \"$group\": { \"_id\": \"$cust_id\", \"total\": { \"$sum\": \"$amount\" } } } ]")]
        public InArgument<string> PipelineJson { get; set; }

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

        [DisplayName("Output JSON")]
        public OutArgument<string> OutJson { get; set; }

        [DisplayName("Output DataTable")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [DisplayName("Output Count")]
        public OutArgument<long> OutCount { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string pipelineStr = PipelineJson.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);

                var collection = MongoConnectionHelper.GetCollection(
                    connStr, dbName, collName, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);

                var stagesArray = BsonSerializer.Deserialize<BsonArray>(pipelineStr);
                var pipelineStages = stagesArray.Select(s => (IPipelineStageDefinition)new JsonPipelineStageDefinition<BsonDocument, BsonDocument>(s.ToJson())).ToList();

                var pipelineDef = new PipelineStagePipelineDefinition<BsonDocument, BsonDocument>(pipelineStages);
                var results = collection.Aggregate(pipelineDef).ToList();

                OutCount.Set(context, results.Count);

                var jsonSettings = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson };
                OutJson.Set(context, new BsonArray(results).ToJson(jsonSettings));

                var dt = BsonDataTableConverter.ToDataTable(results, collName + "_Aggregate");
                OutDataTable.Set(context, dt);

                GlobalSettings.MessageLogger.LogMessage("Aggregation pipeline completed. Output count: {0}", results.Count);
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
