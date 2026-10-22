using System;
using GridTools.Javelin.ZoweActivities.Models;
using Xunit;

namespace GridTools.Javelin.ZoweActivities.Tests
{
    public class TdmMainframeMaskingTemplateTests
    {
        [Fact]
        public void GenerateMaskingJcl_WithDefaults_GeneratesExactGTXMSKIJcl()
        {
            var p = new TdmMainframeMaskingParameters();
            string jcl = TdmMainframeJclTemplate.GenerateMaskingJcl(p);

            // Assert Job Card
            Assert.Contains("//GTXMSKI  JOB (41000000),'MASK IAM VBCAD',CLASS=A,MSGCLASS=X,", jcl);
            Assert.Contains("//             NOTIFY=&SYSUID,REGION=0M,USER=I417777", jcl);
            Assert.Contains("//GTLIB JCLLIB ORDER=SYSP.TDM.A.MVSF.LIB.PROC", jcl);

            // Assert STEP 01 - DEFIAM
            Assert.Contains("//DEFIAM   EXEC PGM=IDCAMS", jcl);
            Assert.Contains("DELETE SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.IAM PURGE", jcl);
            Assert.Contains("DEFINE CLUSTER -", jcl);
            Assert.Contains("NAME(SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.IAM)", jcl);
            Assert.Contains("MODEL(PP.VB.VK.VBCADPRO)", jcl);
            Assert.Contains("OWNER($IAM)", jcl);

            // Assert STEP 02 - GTMSK
            Assert.Contains("//GTMSK EXEC PROC=GTMSKFV,LOADLIB='SYSP.TDM.A.MVSF.LIB.LOAD',", jcl);
            Assert.Contains("MSGDS='SYSP.TDM.A.MVSF.MSG.KSDS',", jcl);
            Assert.Contains("PARMLIB='SYSP.TDM.A.MVSF.LIB.PARM',", jcl);
            Assert.Contains("REPHLQ='SYSP.TDM.A.MVSF.TEMP.AUDIT',", jcl);
            Assert.Contains("INDS='PP.VB.VK.VBCADPRO',", jcl);
            Assert.Contains("OUTDS='SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.SEQ',", jcl);
            Assert.Contains("DEFDS='SYSP.TDM.A.MVSF.LIB.DEF(VBCAD)',", jcl);
            Assert.Contains("MAPDS='SYSP.TDM.A.MVSF.LIB.MAP(VBMAP)',", jcl);
            Assert.Contains("LRECL='2300',RECFM='FB',BLKSIZE='27600',", jcl);
            Assert.Contains("AUDPRI='1',AUDSEC='1'", jcl);

            // Assert Parms & Call
            Assert.Contains("LANGUAGE=EN", jcl);
            Assert.Contains("QUOTESTYLE=DOUBLE", jcl);
            Assert.Contains("MAPTYPE=FILE", jcl);
            Assert.Contains("XREFSCHEMA=GRIDTOOL", jcl);
            Assert.Contains("SEEDSCHEMA=GRIDTOOL", jcl);
            Assert.Contains("SUBSETSCHEMA=GRIDTOOL", jcl);
            Assert.Contains("AUDIT=ALL", jcl);
            Assert.Contains("COMMIT=1000", jcl);
            Assert.Contains("PROGRESSCOUNT=100", jcl);
            Assert.Contains("CALL 'SYSP.TDM.A.MVSF.LIB.LOAD(GTXMSKF)'", jcl);

            // Assert STEP 03 - LOADIAM
            Assert.Contains("//LOADIAM  EXEC PGM=IDCAMS,COND=(4,LT)", jcl);
            Assert.Contains("//INPUT    DD DISP=SHR,DSN=SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.SEQ", jcl);
            Assert.Contains("//OUTPUT   DD DISP=SHR,DSN=SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.IAM", jcl);
            Assert.Contains("REPRO INFILE(INPUT) OUTFILE(OUTPUT)", jcl);
        }

        [Fact]
        public void GenerateMaskingJcl_WithCustomParameters_GeneratesCustomizedJcl()
        {
            var p = new TdmMainframeMaskingParameters
            {
                JobName = "CUSTJOB",
                AccountNumber = "99001122",
                JobDescription = "CUSTOM MASKING",
                InputDataset = "PROD.CUSTOMER.IAM",
                OutputDatasetIam = "STAGE.CUSTOMER.MASKED.IAM",
                OutputDatasetStaging = "STAGE.CUSTOMER.MASKED.SEQ",
                DefinitionDataset = "PROD.TDM.DEF(CUSTDEF)",
                MappingDataset = "PROD.TDM.MAP(CUSTMAP)",
                Lrecl = "1500",
                CommitFrequency = 5000,
                ProgressCount = 500
            };

            string jcl = TdmMainframeJclTemplate.GenerateMaskingJcl(p);

            Assert.Contains("//CUSTJOB  JOB (99001122),'CUSTOM MASKING',CLASS=A,MSGCLASS=X,", jcl);
            Assert.Contains("INDS='PROD.CUSTOMER.IAM',", jcl);
            Assert.Contains("OUTDS='STAGE.CUSTOMER.MASKED.SEQ',", jcl);
            Assert.Contains("DEFDS='PROD.TDM.DEF(CUSTDEF)',", jcl);
            Assert.Contains("MAPDS='PROD.TDM.MAP(CUSTMAP)',", jcl);
            Assert.Contains("NAME(STAGE.CUSTOMER.MASKED.IAM)", jcl);
            Assert.Contains("LRECL='1500'", jcl);
            Assert.Contains("COMMIT=5000", jcl);
            Assert.Contains("PROGRESSCOUNT=500", jcl);
        }
    }
}
