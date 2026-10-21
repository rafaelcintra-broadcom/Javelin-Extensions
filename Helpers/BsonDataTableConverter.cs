using System;
using System.Collections.Generic;
using System.Data;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    public static class BsonDataTableConverter
    {
        public static DataTable ToDataTable(IEnumerable<BsonDocument> documents, string tableName = "MongoData")
        {
            var dt = new DataTable(tableName);
            var docList = new List<BsonDocument>(documents);

            if (docList.Count == 0)
                return dt;

            // 1. Discover all unique field names across documents
            var columns = new HashSet<string>();
            foreach (var doc in docList)
            {
                foreach (var elem in doc.Elements)
                {
                    if (!columns.Contains(elem.Name))
                    {
                        columns.Add(elem.Name);
                        dt.Columns.Add(elem.Name, typeof(string));
                    }
                }
            }

            // 2. Populate rows
            dt.BeginLoadData();
            foreach (var doc in docList)
            {
                var row = dt.NewRow();
                foreach (var elem in doc.Elements)
                {
                    if (elem.Value.IsBsonNull)
                    {
                        row[elem.Name] = DBNull.Value;
                    }
                    else if (elem.Value.IsBsonDocument || elem.Value.IsBsonArray)
                    {
                        row[elem.Name] = elem.Value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
                    }
                    else
                    {
                        row[elem.Name] = elem.Value.ToString();
                    }
                }
                dt.Rows.Add(row);
            }
            dt.EndLoadData();

            return dt;
        }
    }
}
