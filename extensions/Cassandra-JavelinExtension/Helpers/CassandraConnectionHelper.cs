using System;
using System.Collections.Concurrent;
using Cassandra;
using GridTools.Javelin.Common;

namespace GridTools.Javelin.CassandraActivities.Helpers
{
    public static class CassandraConnectionHelper
    {
        private static readonly ConcurrentDictionary<string, ISession> SessionCache =
            new ConcurrentDictionary<string, ISession>(StringComparer.OrdinalIgnoreCase);

        public static ISession GetSession(
            string connectionStringOrProfile,
            string keyspace,
            string portalUrl = null,
            string tdmToken = null,
            string tdmUser = null,
            string tdmPass = null,
            long? projectId = null,
            bool enableDebugLogs = false)
        {
            if (string.IsNullOrWhiteSpace(connectionStringOrProfile))
            {
                throw new ArgumentException("A connection string ou nome do Connection Profile do Cassandra é obrigatório.");
            }

            string target = connectionStringOrProfile.Trim();
            string host = "localhost";
            int port = 9042;
            string username = null;
            string password = null;

            // 1. Tenta resolver via TDM Connection Profile se não for formato direto de host/IP
            if (!target.Contains(";") && !target.Contains("=") && !target.Contains(","))
            {
                try
                {
                    var profile = TdmPortalConnectionProfileResolver.ResolveProfile(
                        target, portalUrl, tdmToken, tdmUser, tdmPass, projectId, enableDebugLogs);

                    if (profile != null)
                    {
                        if (!string.IsNullOrWhiteSpace(profile.Host))
                        {
                            var parts = profile.Host.Split(':');
                            host = parts[0];
                            if (parts.Length > 1 && int.TryParse(parts[1], out int p)) port = p;
                        }

                        if (profile.Port > 0)
                        {
                            port = profile.Port;
                        }

                        username = profile.Username;
                        password = profile.Password;
                        if (string.IsNullOrWhiteSpace(keyspace) && !string.IsNullOrWhiteSpace(profile.DatabaseName))
                        {
                            keyspace = profile.DatabaseName;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (enableDebugLogs)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[CassandraConnection][DEBUG] Falha ao resolver perfil '{0}': {1}", target, ex.Message);
                    }
                }
            }
            else
            {
                // Parsing simples de string de conexão (host:port ou contact points)
                var parts = target.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kvp = part.Split('=');
                    if (kvp.Length == 2)
                    {
                        string k = kvp[0].Trim().ToLowerInvariant();
                        string v = kvp[1].Trim();
                        if (k == "host" || k == "contact points" || k == "server") host = v;
                        else if (k == "port" && int.TryParse(v, out int pt)) port = pt;
                        else if (k == "user" || k == "username") username = v;
                        else if (k == "password" || k == "pwd") password = v;
                        else if (k == "keyspace" && string.IsNullOrWhiteSpace(keyspace)) keyspace = v;
                    }
                    else if (kvp.Length == 1 && !part.Contains("="))
                    {
                        var hp = part.Split(':');
                        host = hp[0].Trim();
                        if (hp.Length > 1 && int.TryParse(hp[1].Trim(), out int pt)) port = pt;
                    }
                }
            }

            string cacheKey = $"{host}:{port}:{username}:{keyspace}";
            if (SessionCache.TryGetValue(cacheKey, out var cachedSession) && !cachedSession.IsDisposed)
            {
                return cachedSession;
            }

            var builder = Cluster.Builder()
                .AddContactPoint(host)
                .WithPort(port);

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                builder.WithCredentials(username, password);
            }

            var cluster = builder.Build();
            var session = string.IsNullOrWhiteSpace(keyspace) ? cluster.Connect() : cluster.Connect(keyspace);

            SessionCache[cacheKey] = session;
            return session;
        }
    }
}
