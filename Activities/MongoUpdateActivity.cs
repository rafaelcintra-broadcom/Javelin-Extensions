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
    [DisplayName("MongoDB Update")]
    [Description("Update documents matching a filter in a MongoDB collection")]
    public class MongoUpdateActivity : JavelinNativeActivity
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
        [DisplayName("Filter (JSON)")]
        [Description("Criteria to match documents, e.g. {\"status\": \"PENDING\"}")]
        public InArgument<string> FilterJson { get; set; }

        [RequiredArgument]
        [DisplayName("Update Definition (JSON)")]
        [Description("Update operations, e.g. {\"$set\": {\"status\": \"PROCESSED\"}}")]
        public InArgument<string> UpdateJson { get; set; }

        [DisplayName("Update Many")]
        [Description("True to update all matching documents; False to update only the first one.")]
        [DefaultValue(false)]
        public InArgument<bool> IsUpdateMany { get; set; }

        [DisplayName("Is Upsert")]
        [Description("True to insert a new document if no match is found.")]
        [DefaultValue(false)]
        public InArgument<bool> IsUpsert { get; set; }

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

        [DisplayName("Matched Count")]
        public OutArgument<long> MatchedCount { get; set; }

        [DisplayName("Modified Count")]
        public OutArgument<long> ModifiedCount { get; set; }

        [DisplayName("Upserted ID")]
        public OutArgument<string> UpsertedId { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string filterStr = FilterJson.Get(context);
                string updateStr = UpdateJson.Get(context);
                bool isUpdateMany = IsUpdateMany.Get(context);
                bool isUpsert = IsUpsert.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);

                var collection = MongoConnectionHelper.GetCollection(
                    connStr, dbName, collName, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);

                var filterDoc = BsonSerializer.Deserialize<BsonDocument>(filterStr);
                var updateDoc = BsonSerializer.Deserialize<BsonDocument>(updateStr);

                var options = new UpdateOptions { IsUpsert = isUpsert };

                if (isUpdateMany)
                {
                    var result = collection.UpdateMany(filterDoc, updateDoc, options);
                    MatchedCount.Set(context, result.MatchedCount);
                    ModifiedCount.Set(context, result.ModifiedCount);
                    UpsertedId.Set(context, result.UpsertedId?.ToString() ?? string.Empty);
                    GlobalSettings.MessageLogger.LogMessage("UpdateMany matched: {0}, modified: {1}", result.MatchedCount, result.ModifiedCount);
                }
                else
                {
                    var result = collection.UpdateOne(filterDoc, updateDoc, options);
                    MatchedCount.Set(context, result.MatchedCount);
                    ModifiedCount.Set(context, result.ModifiedCount);
                    UpsertedId.Set(context, result.UpsertedId?.ToString() ?? string.Empty);
                    GlobalSettings.MessageLogger.LogMessage("UpdateOne matched: {0}, modified: {1}", result.MatchedCount, result.ModifiedCount);
                }
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
