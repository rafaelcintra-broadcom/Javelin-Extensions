using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GridTools.Javelin.ZoweActivities.Models
{
    public class ZoweJobStatus
    {
        [JsonProperty("jobid")]
        public string JobId { get; set; }

        [JsonProperty("jobname")]
        public string JobName { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("retcode")]
        public string ReturnCode { get; set; }

        [JsonProperty("owner")]
        public string Owner { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("subsystem")]
        public string Subsystem { get; set; }

        [JsonProperty("exec-node")]
        public string ExecNode { get; set; }
    }

    public class ZoweSpoolFile
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("stepname")]
        public string StepName { get; set; }

        [JsonProperty("procstep")]
        public string ProcStep { get; set; }

        [JsonProperty("ddname")]
        public string DdName { get; set; }

        [JsonProperty("records")]
        public int? RecordCount { get; set; }

        [JsonProperty("byte-count")]
        public long? ByteCount { get; set; }
    }

    public class ZoweDatasetItem
    {
        [JsonProperty("dsname")]
        public string DsName { get; set; }

        [JsonProperty("volume")]
        public string Volume { get; set; }

        [JsonProperty("dsorg")]
        public string DsOrg { get; set; }

        [JsonProperty("recfm")]
        public string Recfm { get; set; }

        [JsonProperty("lrecl")]
        public int? Lrecl { get; set; }

        [JsonProperty("blksz")]
        public int? BlkSize { get; set; }

        [JsonProperty("member")]
        public string Member { get; set; }
    }

    public class ZoweCliExecutionResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public string FormattedError { get; set; }
        public string RawCommand { get; set; }
    }

    public class ZoweProfileOptions
    {
        public string ZosmfProfile { get; set; }
        public string BaseProfile { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string TokenValue { get; set; }
        public string TokenType { get; set; }
        public string CertPath { get; set; }
        public string CertKeyPath { get; set; }
        public bool? RejectUnauthorized { get; set; }
        public string BasePath { get; set; }
    }
}
