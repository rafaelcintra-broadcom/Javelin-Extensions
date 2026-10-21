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
    [DisplayName("MongoDB Delete")]
    [Description("Delete documents matching a filter from a MongoDB collection")]
    public class MongoDeleteActivity : JavelinNativeActivity
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
        [Description("Filter condition, e.g. {\"testRunId\": 1001}")]
        public InArgument<string> FilterJson { get; set; }

        [DisplayName("Delete Many")]
        [Description("True to delete all matching documents; False to delete only the first one.")]
        [DefaultValue(false)]
        public InArgument<bool> IsDeleteMany { get; set; }

        [DisplayName("Deleted Count")]
        public OutArgument<long> DeletedCount { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string filterStr = FilterJson.Get(context);
                bool isDeleteMany = IsDeleteMany.Get(context);

                var collection = MongoConnectionHelper.GetCollection(connStr, dbName, collName);
                var filterDoc = BsonSerializer.Deserialize<BsonDocument>(filterStr);

                if (isDeleteMany)
                {
                    var result = collection.DeleteMany(filterDoc);
                    DeletedCount.Set(context, result.DeletedCount);
                    GlobalSettings.MessageLogger.LogMessage("DeleteMany deleted {0} document(s)", result.DeletedCount);
                }
                else
                {
                    var result = collection.DeleteOne(filterDoc);
                    DeletedCount.Set(context, result.DeletedCount);
                    GlobalSettings.MessageLogger.LogMessage("DeleteOne deleted {0} document(s)", result.DeletedCount);
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
