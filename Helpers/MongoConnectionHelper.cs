using System;
using System.Collections.Concurrent;
using GridTools.Javelin.Common;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    public static class MongoConnectionHelper
    {
        private static readonly ConcurrentDictionary<string, IMongoClient> ClientCache = 
            new ConcurrentDictionary<string, IMongoClient>(StringComparer.OrdinalIgnoreCase);

        public static IMongoDatabase GetDatabase(string rawConnectionStringOrProfile, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionStringOrProfile))
                throw new ArgumentException("MongoDB ConnectionString or Connection Profile Name cannot be null or empty.");

            // 1. Tenta resolver via API do TDM se for nome de perfil (ex: MONGO_PROD_CLUSTER)
            string actualDbName;
            string resolvedUri = TdmPortalConnectionProfileResolver.ResolveMongoConnectionString(
                rawConnectionStringOrProfile, out actualDbName, databaseName);

            string effectiveDb = !string.IsNullOrWhiteSpace(databaseName) ? databaseName : actualDbName;
            if (string.IsNullOrWhiteSpace(effectiveDb))
                effectiveDb = "admin";

            // 2. Decripta senhas e variáveis protegidas do Javelin (ex: %password_var%)
            string decryptedConnStr = new JavelinPasswordDecryptor().SubstitutePasswordsInString(resolvedUri);

            var client = ClientCache.GetOrAdd(decryptedConnStr, connStr =>
            {
                var settings = MongoClientSettings.FromConnectionString(connStr);
                settings.ApplicationName = "Javelin-TDM-Extension";
                return new MongoClient(settings);
            });

            return client.GetDatabase(effectiveDb);
        }

        public static IMongoCollection<MongoDB.Bson.BsonDocument> GetCollection(
            string connectionStringOrProfile, 
            string databaseName, 
            string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentException("MongoDB CollectionName cannot be null or empty.");

            var db = GetDatabase(connectionStringOrProfile, databaseName);
            return db.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);
        }
    }
}
