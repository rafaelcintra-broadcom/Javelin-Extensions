using System;
using System.Data;
using GridTools.Javelin.ZoweActivities.Helpers;
using Xunit;

namespace GridTools.Javelin.ZoweActivities.Tests
{
    public class ZoweOutputParserTests
    {
        [Fact]
        public void ExtractJsonFromCliOutput_WithSurroundingCliLogs_ExtractsArrayJson()
        {
            string rawOutput = "Logging in to z/OSMF...\n" +
                               "[{\"dsname\": \"HLQ.PROD.DATA\", \"volume\": \"VOL001\"}, {\"dsname\": \"HLQ.PROD.JCL\", \"volume\": \"VOL002\"}]\n" +
                               "Command executed in 1.2s";

            string extracted = ZoweOutputParser.ExtractJsonFromCliOutput(rawOutput);

            Assert.StartsWith("[", extracted);
            Assert.EndsWith("]", extracted);
            Assert.Contains("HLQ.PROD.DATA", extracted);
        }

        [Fact]
        public void ExtractJsonFromCliOutput_WithSurroundingCliLogs_ExtractsObjectJson()
        {
            string rawOutput = "Connecting...\n" +
                               "{\"jobid\": \"JOB12345\", \"jobname\": \"TDMJOB01\", \"status\": \"OUTPUT\", \"retcode\": \"CC 0000\"}\n" +
                               "Done.";

            string extracted = ZoweOutputParser.ExtractJsonFromCliOutput(rawOutput);

            Assert.StartsWith("{", extracted);
            Assert.EndsWith("}", extracted);
            Assert.Contains("JOB12345", extracted);
        }

        [Fact]
        public void ExtractJsonFromCliOutput_WhenPlainString_ReturnsTrimmedString()
        {
            string rawText = "  Plain output text without JSON  ";
            string extracted = ZoweOutputParser.ExtractJsonFromCliOutput(rawText);

            Assert.Equal("Plain output text without JSON", extracted);
        }

        [Fact]
        public void ParseJsonArrayToDataTable_WithValidJsonArray_CreatesPopulatedDataTable()
        {
            string json = "[{\"dsname\": \"HLQ.TEST.DATA\", \"recfm\": \"FB\", \"lrecl\": 80}, {\"dsname\": \"HLQ.TEST.VB\", \"recfm\": \"VB\", \"lrecl\": 133}]";

            DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, "Datasets");

            Assert.NotNull(dt);
            Assert.Equal("Datasets", dt.TableName);
            Assert.Equal(2, dt.Rows.Count);
            Assert.True(dt.Columns.Contains("dsname"));
            Assert.True(dt.Columns.Contains("recfm"));
            Assert.True(dt.Columns.Contains("lrecl"));

            Assert.Equal("HLQ.TEST.DATA", dt.Rows[0]["dsname"].ToString());
            Assert.Equal("FB", dt.Rows[0]["recfm"].ToString());
            Assert.Equal("80", dt.Rows[0]["lrecl"].ToString());

            Assert.Equal("HLQ.TEST.VB", dt.Rows[1]["dsname"].ToString());
            Assert.Equal("VB", dt.Rows[1]["recfm"].ToString());
        }

        [Fact]
        public void ParseJsonArrayToDataTable_WithSingleJsonObject_CreatesSingleRowDataTable()
        {
            string json = "{\"jobid\": \"JOB99999\", \"status\": \"ACTIVE\", \"owner\": \"IBMUSER\"}";

            DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, "Job");

            Assert.NotNull(dt);
            Assert.Single(dt.Rows);
            Assert.Equal("JOB99999", dt.Rows[0]["jobid"].ToString());
            Assert.Equal("ACTIVE", dt.Rows[0]["status"].ToString());
            Assert.Equal("IBMUSER", dt.Rows[0]["owner"].ToString());
        }

        [Fact]
        public void ParseJsonArrayToDataTable_WhenInvalidJson_FallsBackToLineParser()
        {
            string text = "LINE 1 OF OUTPUT\nLINE 2 OF OUTPUT";

            DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(text, "Fallback");

            Assert.NotNull(dt);
            Assert.Equal(2, dt.Rows.Count);
            Assert.True(dt.Columns.Contains("LineNumber"));
            Assert.True(dt.Columns.Contains("Content"));
            Assert.Equal("LINE 1 OF OUTPUT", dt.Rows[0]["Content"].ToString());
            Assert.Equal(1, dt.Rows[0]["LineNumber"]);
        }

        [Fact]
        public void ParseTextLinesToDataTable_WithSpoolText_ParsesLinesCorrectly()
        {
            string spool = "1 //JOB01 JOB\n2 //STEP01 EXEC PGM=IEFBR14\n3 //SYSPRINT DD SYSOUT=*";

            DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(spool, "Spool");

            Assert.NotNull(dt);
            Assert.Equal(3, dt.Rows.Count);
            Assert.Equal(1, dt.Rows[0]["LineNumber"]);
            Assert.Equal("1 //JOB01 JOB", dt.Rows[0]["Content"].ToString());
            Assert.Equal(3, dt.Rows[2]["LineNumber"]);
            Assert.Equal("3 //SYSPRINT DD SYSOUT=*", dt.Rows[2]["Content"].ToString());
        }

        [Fact]
        public void ParseTextLinesToDataTable_WhenEmpty_ReturnsEmptyDataTableWithColumns()
        {
            DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable("", "Empty");

            Assert.NotNull(dt);
            Assert.Empty(dt.Rows);
            Assert.True(dt.Columns.Contains("LineNumber"));
            Assert.True(dt.Columns.Contains("Content"));
        }
    }
}
