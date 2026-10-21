using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Insert")]
    [Description("Insert one or multiple documents into a MongoDB collection")]
    public class MongoInsertActivity : JavelinNativeActivity
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

        [DisplayName("Document(s) JSON")]
        [Description("Single JSON object {\"a\": 1} or JSON array [{\"a\": 1}, {\"a\": 2}]")]
        public InArgument<string> DocumentJson { get; set; }

        [DisplayName("Input DataTable")]
        [Description("Optional: Insert rows directly from a Javelin DataTable")]
        public InArgument<DataTable> InputDataTable { get; set; }

        [DisplayName("Inserted Count")]
        public OutArgument<int> InsertedCount { get; set; }

        [DisplayName("Inserted IDs (JSON Array)")]
        public OutArgument<string> InsertedIdsJson { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string docJson = DocumentJson.Get(context);
                DataTable inputDt = InputDataTable.Get(context);

                var collection = MongoConnectionHelper.GetCollection(connStr, dbName, collName);
                var docsToInsert = new List<BsonDocument>();

                if (!string.IsNullOrWhiteSpace(docJson))
                {
                    docJson = docJson.Trim();
                    if (docJson.StartsWith("["))
                    {
                        var bsonArray = BsonSerializer.Deserialize<BsonArray>(docJson);
                        docsToInsert.AddRange(bsonArray.Select(b => b.AsBsonDocument));
                    }
                    else
                    {
                        var singleDoc = BsonSerializer.Deserialize<BsonDocument>(docJson);
                        docsToInsert.Add(singleDoc);
                    }
                }
                else if (inputDt != null && inputDt.Rows.Count > 0)
                {
                    foreach (DataRow row in inputDt.Rows)
                    {
                        var doc = new BsonDocument();
                        foreach (DataColumn col in inputDt.Columns)
                        {
                            if (!row.IsNull(col))
                            {
                                doc.Add(col.ColumnName, BsonValue.Create(row[col]));
                            }
                        }
                        docsToInsert.Add(doc);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Either DocumentJson or InputDataTable must be provided.");
                }

                if (docsToInsert.Count == 1)
                {
                    collection.InsertOne(docsToInsert[0]);
                    InsertedCount.Set(context, 1);
                    var idArray = new BsonArray { docsToInsert[0].Contains("_id") ? docsToInsert[0]["_id"] : BsonNull.Value };
                    InsertedIdsJson.Set(context, idArray.ToJson());
                }
                else if (docsToInsert.Count > 1)
                {
                    collection.InsertMany(docsToInsert);
                    InsertedCount.Set(context, docsToInsert.Count);
                    var idArray = new BsonArray(docsToInsert.Select(d => d.Contains("_id") ? d["_id"] : BsonNull.Value));
                    InsertedIdsJson.Set(context, idArray.ToJson());
                }

                GlobalSettings.MessageLogger.LogMessage("Successfully inserted {0} document(s) into {1}.{2}", docsToInsert.Count, dbName, collName);
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
