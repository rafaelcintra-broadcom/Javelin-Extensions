using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Linq;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Find")]
    [Description("Find and retrieve documents from a MongoDB collection")]
    public class MongoFindActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("Connection String")]
        [Description("MongoDB Connection URI (e.g. mongodb://localhost:27017 or mongodb+srv://...)")]
        public InArgument<string> ConnectionString { get; set; }

        [RequiredArgument]
        [DisplayName("Database Name")]
        public InArgument<string> DatabaseName { get; set; }

        [RequiredArgument]
        [DisplayName("Collection Name")]
        public InArgument<string> CollectionName { get; set; }

        [DisplayName("Filter (JSON or SQL WHERE)")]
        [Description("BSON/JSON query filter (ex: {\"status\": \"ACTIVE\"}) OU cláusula WHERE clássica (ex: status = 'ATIVO' AND idade >= 18). Default is {} for all documents.")]
        [DefaultValue("{}")]
        public InArgument<string> FilterJson { get; set; }

        [DisplayName("WHERE Clause (SQL-Style)")]
        [Description("Cláusula WHERE clássica opcional (ex: status = 'ATIVO' AND saldo > 1000). Tem precedência se preenchida.")]
        public InArgument<string> WhereClause { get; set; }

        [DisplayName("Projection (JSON)")]
        [Description("Fields to include/exclude, e.g. {\"name\": 1, \"email\": 1, \"_id\": 0}")]
        public InArgument<string> ProjectionJson { get; set; }

        [DisplayName("Sort (JSON)")]
        [Description("Sorting criteria, e.g. {\"createdAt\": -1}")]
        public InArgument<string> SortJson { get; set; }

        [DisplayName("Limit")]
        [Description("Maximum number of documents to return (0 or empty for no limit)")]
        public InArgument<int?> Limit { get; set; }

        [DisplayName("Skip")]
        [Description("Number of documents to skip")]
        public InArgument<int?> Skip { get; set; }

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
                string whereClause = WhereClause.Get(context);
                string filterStr = FilterJson.Get(context);
                string projectionStr = ProjectionJson.Get(context);
                string sortStr = SortJson.Get(context);
                int? limit = Limit.Get(context);
                int? skip = Skip.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);

                string effectiveFilter = !string.IsNullOrWhiteSpace(whereClause) ? whereClause : filterStr;
                if (string.IsNullOrWhiteSpace(effectiveFilter))
                    effectiveFilter = "{}";

                var filter = MongoSqlFilterParser.BuildFilter(effectiveFilter);
                GlobalSettings.MessageLogger.LogMessage("Executing MongoDB Find on {0}.{1} with filter (MQL): {2}", dbName, collName, filter.ToJson());

                var collection = MongoConnectionHelper.GetCollection(
                    connStr, dbName, collName, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);
                var findFluent = collection.Find(filter);

                if (!string.IsNullOrWhiteSpace(projectionStr))
                {
                    var projDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(projectionStr);
                    findFluent = findFluent.Project(projDoc);
                }

                if (!string.IsNullOrWhiteSpace(sortStr))
                {
                    var sortDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(sortStr);
                    findFluent = findFluent.Sort(sortDoc);
                }

                if (skip.HasValue && skip.Value > 0)
                {
                    findFluent = findFluent.Skip(skip.Value);
                }

                if (limit.HasValue && limit.Value > 0)
                {
                    findFluent = findFluent.Limit(limit.Value);
                }

                var results = findFluent.ToList();

                OutCount.Set(context, results.Count);

                var jsonSettings = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson };
                var jsonArray = new BsonArray(results).ToJson(jsonSettings);
                OutJson.Set(context, jsonArray);

                var dataTable = BsonDataTableConverter.ToDataTable(results, collName);
                OutDataTable.Set(context, dataTable);

                GlobalSettings.MessageLogger.LogMessage("MongoDB Find completed. Fetched {0} documents.", results.Count);
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
