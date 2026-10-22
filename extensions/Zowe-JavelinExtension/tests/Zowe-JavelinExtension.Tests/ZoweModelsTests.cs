using System;
using GridTools.Javelin.ZoweActivities.Models;
using Newtonsoft.Json;
using Xunit;

namespace GridTools.Javelin.ZoweActivities.Tests
{
    public class ZoweModelsTests
    {
        [Fact]
        public void ZoweJobStatus_Deserialization_MapsPropertiesProperly()
        {
            string json = @"{
                ""jobid"": ""JOB00432"",
                ""jobname"": ""TDMTEST"",
                ""status"": ""OUTPUT"",
                ""retcode"": ""CC 0000"",
                ""owner"": ""IBMUSER"",
                ""type"": ""JOB"",
                ""subsystem"": ""JES2"",
                ""exec-node"": ""N1""
            }";

            var model = JsonConvert.DeserializeObject<ZoweJobStatus>(json);

            Assert.NotNull(model);
            Assert.Equal("JOB00432", model.JobId);
            Assert.Equal("TDMTEST", model.JobName);
            Assert.Equal("OUTPUT", model.Status);
            Assert.Equal("CC 0000", model.ReturnCode);
            Assert.Equal("IBMUSER", model.Owner);
            Assert.Equal("JOB", model.Type);
            Assert.Equal("JES2", model.Subsystem);
            Assert.Equal("N1", model.ExecNode);
        }

        [Fact]
        public void ZoweSpoolFile_Deserialization_MapsPropertiesProperly()
        {
            string json = @"{
                ""id"": 102,
                ""stepname"": ""STEP01"",
                ""procstep"": """",
                ""ddname"": ""SYSPRINT"",
                ""records"": 154,
                ""byte-count"": 12430
            }";

            var model = JsonConvert.DeserializeObject<ZoweSpoolFile>(json);

            Assert.NotNull(model);
            Assert.Equal(102, model.Id);
            Assert.Equal("STEP01", model.StepName);
            Assert.Equal("SYSPRINT", model.DdName);
            Assert.Equal(154, model.RecordCount);
            Assert.Equal(12430, model.ByteCount);
        }

        [Fact]
        public void ZoweDatasetItem_Deserialization_MapsPropertiesProperly()
        {
            string json = @"{
                ""dsname"": ""TDM.PROD.CUSTOMER.DATA"",
                ""volume"": ""VOL101"",
                ""dsorg"": ""PS"",
                ""recfm"": ""FB"",
                ""lrecl"": 120,
                ""blksz"": 27960,
                ""member"": ""MEMBER1""
            }";

            var model = JsonConvert.DeserializeObject<ZoweDatasetItem>(json);

            Assert.NotNull(model);
            Assert.Equal("TDM.PROD.CUSTOMER.DATA", model.DsName);
            Assert.Equal("VOL101", model.Volume);
            Assert.Equal("PS", model.DsOrg);
            Assert.Equal("FB", model.Recfm);
            Assert.Equal(120, model.Lrecl);
            Assert.Equal(27960, model.BlkSize);
            Assert.Equal("MEMBER1", model.Member);
        }
    }
}
