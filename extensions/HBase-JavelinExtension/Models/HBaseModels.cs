using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Models
{
    /// <summary>
    /// Represents the HBase REST JSON payload root structure: {"Row": [ ... ]}
    /// </summary>
    public class HBaseRowSet
    {
        [JsonProperty("Row")]
        public List<HBaseRow> Rows { get; set; } = new List<HBaseRow>();
    }

    /// <summary>
    /// Represents an HBase row with Base64 key and columns
    /// </summary>
    public class HBaseRow
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("Cell")]
        public List<HBaseCell> Cells { get; set; } = new List<HBaseCell>();
    }

    /// <summary>
    /// Represents an HBase column cell with Base64 column qualifier and Base64 value
    /// </summary>
    public class HBaseCell
    {
        [JsonProperty("column")]
        public string Column { get; set; }

        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public long? Timestamp { get; set; }

        [JsonProperty("$")]
        public string Value { get; set; }
    }

    /// <summary>
    /// Model for scanner creation endpoint (/table/scanner)
    /// </summary>
    public class HBaseScannerModel
    {
        [JsonProperty("startRow", NullValueHandling = NullValueHandling.Ignore)]
        public string StartRow { get; set; }

        [JsonProperty("stopRow", NullValueHandling = NullValueHandling.Ignore)]
        public string StopRow { get; set; }

        [JsonProperty("batch", NullValueHandling = NullValueHandling.Ignore)]
        public int? Batch { get; set; }

        [JsonProperty("startTime", NullValueHandling = NullValueHandling.Ignore)]
        public long? StartTime { get; set; }

        [JsonProperty("endTime", NullValueHandling = NullValueHandling.Ignore)]
        public long? EndTime { get; set; }

        [JsonProperty("maxVersions", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxVersions { get; set; }

        [JsonProperty("column", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Columns { get; set; }

        [JsonProperty("filter", NullValueHandling = NullValueHandling.Ignore)]
        public string Filter { get; set; }
    }
}
