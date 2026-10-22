using System;
using System.Text;

namespace GridTools.Javelin.ZoweActivities.Models
{
    public class TdmMainframeMaskingParameters
    {
        // Jobcard
        public string JobName { get; set; } = "GTXMSKI";
        public string AccountNumber { get; set; } = "41000000";
        public string JobDescription { get; set; } = "MASK IAM VBCAD";
        public string JobClass { get; set; } = "A";
        public string MsgClass { get; set; } = "X";
        public string NotifyUser { get; set; } = "&SYSUID";
        public string Region { get; set; } = "0M";
        public string ExecutionUser { get; set; } = "I417777";

        // Proclib
        public string ProcLibOrder { get; set; } = "SYSP.TDM.A.MVSF.LIB.PROC";

        // Datasets & Libraries
        public string LoadLib { get; set; } = "SYSP.TDM.A.MVSF.LIB.LOAD";
        public string MsgDs { get; set; } = "SYSP.TDM.A.MVSF.MSG.KSDS";
        public string ParmLib { get; set; } = "SYSP.TDM.A.MVSF.LIB.PARM";
        public string ReportHlq { get; set; } = "SYSP.TDM.A.MVSF.TEMP.AUDIT";

        // Input & Output Datasets
        public string InputDataset { get; set; } = "PP.VB.VK.VBCADPRO";
        public string OutputDatasetStaging { get; set; } = "SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.SEQ";
        public string OutputDatasetIam { get; set; } = "SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.IAM";
        public string DefinitionDataset { get; set; } = "SYSP.TDM.A.MVSF.LIB.DEF(VBCAD)";
        public string MappingDataset { get; set; } = "SYSP.TDM.A.MVSF.LIB.MAP(VBMAP)";

        // File Allocation Attributes
        public string Lrecl { get; set; } = "2300";
        public string Recfm { get; set; } = "FB";
        public string BlkSize { get; set; } = "27600";
        public string AuditPri { get; set; } = "1";
        public string AuditSec { get; set; } = "1";

        // Step 01 - IDCAMS / IAM Model
        public string IamModelDataset { get; set; } = "PP.VB.VK.VBCADPRO";
        public string IamOwner { get; set; } = "$IAM";

        // Step 02 - GTMSK Parameters
        public string Language { get; set; } = "EN";
        public string QuoteStyle { get; set; } = "DOUBLE";
        public string MapType { get; set; } = "FILE";
        public string XrefSchema { get; set; } = "GRIDTOOL";
        public string SeedSchema { get; set; } = "GRIDTOOL";
        public string SubsetSchema { get; set; } = "GRIDTOOL";
        public string AuditLevel { get; set; } = "ALL";
        public int CommitFrequency { get; set; } = 1000;
        public int ProgressCount { get; set; } = 100;
        public string ProgramToCall { get; set; } = "GTXMSKF";
    }

    public static class TdmMainframeJclTemplate
    {
        public static string GenerateMaskingJcl(TdmMainframeMaskingParameters p)
        {
            if (p == null) p = new TdmMainframeMaskingParameters();

            var sb = new StringBuilder();

            // Job Card
            string userClause = !string.IsNullOrWhiteSpace(p.ExecutionUser) ? $",USER={p.ExecutionUser}" : "";
            sb.AppendLine($"//{p.JobName,-8} JOB ({p.AccountNumber}),'{p.JobDescription}',CLASS={p.JobClass},MSGCLASS={p.MsgClass},");
            sb.AppendLine($"//             NOTIFY={p.NotifyUser},REGION={p.Region}{userClause}");
            sb.AppendLine($"//GTLIB JCLLIB ORDER={p.ProcLibOrder}");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine("//* STEP 01: DEFINE O ARQUIVO IAM HERDANDO OS METADADOS DE ENTRADA    *");
            sb.AppendLine($"//*          (KEYS, RECORDSIZE, CISZ COPIADOS DE {p.IamModelDataset})   *");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine("//DEFIAM   EXEC PGM=IDCAMS");
            sb.AppendLine("//SYSPRINT DD SYSOUT=*");
            sb.AppendLine("//IAMPRINT DD SYSOUT=*");
            sb.AppendLine("//SYSIN    DD *");
            sb.AppendLine($"  DELETE {p.OutputDatasetIam} PURGE");
            sb.AppendLine("  SET MAXCC = 0");
            sb.AppendLine("");
            sb.AppendLine("  DEFINE CLUSTER -");
            sb.AppendLine($"    (NAME({p.OutputDatasetIam}) -");
            sb.AppendLine($"     MODEL({p.IamModelDataset}) -");
            sb.AppendLine($"     OWNER({p.IamOwner}))");
            sb.AppendLine("/*");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine("//* STEP 02: EXECUTA O MASQUEAMENTO DO TDM GERANDO O ARQUIVO STAGING  *");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine($"//GTMSK EXEC PROC=GTMSKFV,LOADLIB='{p.LoadLib}',");
            sb.AppendLine($"//  MSGDS='{p.MsgDs}',");
            sb.AppendLine($"//  PARMLIB='{p.ParmLib}',");
            sb.AppendLine($"//  REPHLQ='{p.ReportHlq}',");
            sb.AppendLine($"//  INDS='{p.InputDataset}',");
            sb.AppendLine($"//  OUTDS='{p.OutputDatasetStaging}',");
            sb.AppendLine($"//  DEFDS='{p.DefinitionDataset}',");
            sb.AppendLine($"//  MAPDS='{p.MappingDataset}',");
            sb.AppendLine($"//  LRECL='{p.Lrecl}',RECFM='{p.Recfm}',BLKSIZE='{p.BlkSize}',");
            sb.AppendLine($"//  AUDPRI='{p.AuditPri}',AUDSEC='{p.AuditSec}'");
            sb.AppendLine("//*");
            sb.AppendLine("//STEP03.PARMCD DD *");
            sb.AppendLine("--LANGUAGE");
            sb.AppendLine($"LANGUAGE={p.Language}");
            sb.AppendLine("--QUOTESTYLE=DOUBLE OR SINGLE");
            sb.AppendLine($"QUOTESTYLE={p.QuoteStyle}");
            sb.AppendLine("/*");
            sb.AppendLine("//STEP04.PARMCD DD *");
            sb.AppendLine("--LANGUAGE");
            sb.AppendLine($"LANGUAGE={p.Language}");
            sb.AppendLine("--QUOTESTYLE=DOUBLE OR SINGLE");
            sb.AppendLine($"QUOTESTYLE={p.QuoteStyle}");
            sb.AppendLine($"MAPTYPE={p.MapType}");
            sb.AppendLine("/*");
            sb.AppendLine("//STEP05.PARMCD DD *");
            sb.AppendLine($"--LANGUAGE={p.Language} (THE DEFAULT)");
            sb.AppendLine($"XREFSCHEMA={p.XrefSchema}");
            sb.AppendLine($"SEEDSCHEMA={p.SeedSchema}");
            sb.AppendLine($"SUBSETSCHEMA={p.SubsetSchema}");
            sb.AppendLine($"AUDIT={p.AuditLevel}");
            sb.AppendLine($"COMMIT={p.CommitFrequency}");
            sb.AppendLine($"PROGRESSCOUNT={p.ProgressCount}");
            sb.AppendLine("/*");
            sb.AppendLine("//STEP05.SYSTSIN  DD *");
            sb.AppendLine($"  CALL '{p.LoadLib}({p.ProgramToCall})'");
            sb.AppendLine("/*");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine("//* STEP 03: REPRO CARREGANDO OS DADOS MASCARADOS NO ARQUIVO IAM      *");
            sb.AppendLine("//*-------------------------------------------------------------------*");
            sb.AppendLine("//LOADIAM  EXEC PGM=IDCAMS,COND=(4,LT)");
            sb.AppendLine("//SYSPRINT DD SYSOUT=*");
            sb.AppendLine("//IAMPRINT DD SYSOUT=*");
            sb.AppendLine($"//INPUT    DD DISP=SHR,DSN={p.OutputDatasetStaging}");
            sb.AppendLine($"//OUTPUT   DD DISP=SHR,DSN={p.OutputDatasetIam}");
            sb.AppendLine("//SYSIN    DD *");
            sb.AppendLine("  REPRO INFILE(INPUT) OUTFILE(OUTPUT)");
            sb.AppendLine("/*");

            return sb.ToString();
        }
    }
}
