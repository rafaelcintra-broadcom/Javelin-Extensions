using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe TDM Masking Job (GTXMSKI / IAM)")]
    [Description("Submits parameterized TDM Mainframe File Masking batch job (GTMSKFV / IAM / IDCAMS / REPRO), waits for execution and returns JobId and ReturnCode")]
    public class ZoweTdmMaskingJobActivity : ZoweBaseActivity
    {
        // --- Category: Job Card & Mainframe Execution ---
        [Category("1. Job Card & JES")]
        [DisplayName("Job Name")]
        [DefaultValue("GTXMSKI")]
        public InArgument<string> JobName { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Account Number")]
        [DefaultValue("41000000")]
        public InArgument<string> AccountNumber { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Job Description")]
        [DefaultValue("MASK IAM VBCAD")]
        public InArgument<string> JobDescription { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Job Class")]
        [DefaultValue("A")]
        public InArgument<string> JobClass { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Msg Class")]
        [DefaultValue("X")]
        public InArgument<string> MsgClass { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Notify User")]
        [DefaultValue("&SYSUID")]
        public InArgument<string> NotifyUser { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Region Size")]
        [DefaultValue("0M")]
        public InArgument<string> Region { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("Execution User (RACF/ACF2 USER=)")]
        [DefaultValue("I417777")]
        public InArgument<string> ExecutionUser { get; set; }

        [Category("1. Job Card & JES")]
        [DisplayName("ProcLib JCLLIB Order")]
        [DefaultValue("SYSP.TDM.A.MVSF.LIB.PROC")]
        public InArgument<string> ProcLibOrder { get; set; }

        // --- Category: TDM Libraries ---
        [Category("2. TDM System Libraries")]
        [DisplayName("TDM Load Library (LOADLIB)")]
        [DefaultValue("SYSP.TDM.A.MVSF.LIB.LOAD")]
        public InArgument<string> LoadLib { get; set; }

        [Category("2. TDM System Libraries")]
        [DisplayName("TDM Message Dataset (MSGDS)")]
        [DefaultValue("SYSP.TDM.A.MVSF.MSG.KSDS")]
        public InArgument<string> MsgDs { get; set; }

        [Category("2. TDM System Libraries")]
        [DisplayName("TDM Parm Library (PARMLIB)")]
        [DefaultValue("SYSP.TDM.A.MVSF.LIB.PARM")]
        public InArgument<string> ParmLib { get; set; }

        [Category("2. TDM System Libraries")]
        [DisplayName("Report / Audit HLQ (REPHLQ)")]
        [DefaultValue("SYSP.TDM.A.MVSF.TEMP.AUDIT")]
        public InArgument<string> ReportHlq { get; set; }

        // --- Category: Datasets & Rules ---
        [Category("3. Masking Datasets & Rules")]
        [RequiredArgument]
        [DisplayName("Input Dataset (INDS)")]
        [DefaultValue("PP.VB.VK.VBCADPRO")]
        public InArgument<string> InputDataset { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [RequiredArgument]
        [DisplayName("Output Staging Dataset (OUTDS)")]
        [DefaultValue("SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.SEQ")]
        public InArgument<string> OutputDatasetStaging { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [RequiredArgument]
        [DisplayName("Output IAM Dataset")]
        [DefaultValue("SYSP.TDM.A.MVSF.TEMP.TEST.MASKED.IAM")]
        public InArgument<string> OutputDatasetIam { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [RequiredArgument]
        [DisplayName("Definition Dataset (DEFDS)")]
        [DefaultValue("SYSP.TDM.A.MVSF.LIB.DEF(VBCAD)")]
        public InArgument<string> DefinitionDataset { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [RequiredArgument]
        [DisplayName("Mapping Dataset (MAPDS)")]
        [DefaultValue("SYSP.TDM.A.MVSF.LIB.MAP(VBMAP)")]
        public InArgument<string> MappingDataset { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [DisplayName("IAM Model Dataset")]
        [DefaultValue("PP.VB.VK.VBCADPRO")]
        public InArgument<string> IamModelDataset { get; set; }

        [Category("3. Masking Datasets & Rules")]
        [DisplayName("IAM Owner")]
        [DefaultValue("$IAM")]
        public InArgument<string> IamOwner { get; set; }

        // --- Category: Dataset Format Attributes ---
        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Record Length (LRECL)")]
        [DefaultValue("2300")]
        public InArgument<string> Lrecl { get; set; }

        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Record Format (RECFM)")]
        [DefaultValue("FB")]
        public InArgument<string> Recfm { get; set; }

        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Block Size (BLKSIZE)")]
        [DefaultValue("27600")]
        public InArgument<string> BlkSize { get; set; }

        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Commit Frequency")]
        [DefaultValue(1000)]
        public InArgument<int> CommitFrequency { get; set; }

        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Progress Count")]
        [DefaultValue(100)]
        public InArgument<int> ProgressCount { get; set; }

        [Category("4. Dataset Attributes & Engine")]
        [DisplayName("Audit Level")]
        [DefaultValue("ALL")]
        public InArgument<string> AuditLevel { get; set; }

        // --- Category: Execution Options ---
        [Category("5. Execution")]
        [DisplayName("Wait For Completion")]
        [DefaultValue(true)]
        public InArgument<bool> WaitForCompletion { get; set; }

        // --- Outputs ---
        [Category("Output")]
        [DisplayName("Generated JCL")]
        public OutArgument<string> GeneratedJcl { get; set; }

        [Category("Output")]
        [DisplayName("Job ID")]
        public OutArgument<string> JobId { get; set; }

        [Category("Output")]
        [DisplayName("Return Code")]
        public OutArgument<string> ReturnCode { get; set; }

        [Category("Output")]
        [DisplayName("Spool Output")]
        public OutArgument<string> SpoolOutput { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                var p = new TdmMainframeMaskingParameters();

                string jName = JobName.Get(context);
                if (!string.IsNullOrWhiteSpace(jName)) p.JobName = jName;

                string acct = AccountNumber.Get(context);
                if (!string.IsNullOrWhiteSpace(acct)) p.AccountNumber = acct;

                string desc = JobDescription.Get(context);
                if (!string.IsNullOrWhiteSpace(desc)) p.JobDescription = desc;

                string jClass = JobClass.Get(context);
                if (!string.IsNullOrWhiteSpace(jClass)) p.JobClass = jClass;

                string mClass = MsgClass.Get(context);
                if (!string.IsNullOrWhiteSpace(mClass)) p.MsgClass = mClass;

                string notif = NotifyUser.Get(context);
                if (!string.IsNullOrWhiteSpace(notif)) p.NotifyUser = notif;

                string reg = Region.Get(context);
                if (!string.IsNullOrWhiteSpace(reg)) p.Region = reg;

                string execUser = ExecutionUser.Get(context);
                if (!string.IsNullOrWhiteSpace(execUser)) p.ExecutionUser = execUser;

                string procLib = ProcLibOrder.Get(context);
                if (!string.IsNullOrWhiteSpace(procLib)) p.ProcLibOrder = procLib;

                string load = LoadLib.Get(context);
                if (!string.IsNullOrWhiteSpace(load)) p.LoadLib = load;

                string msg = MsgDs.Get(context);
                if (!string.IsNullOrWhiteSpace(msg)) p.MsgDs = msg;

                string parm = ParmLib.Get(context);
                if (!string.IsNullOrWhiteSpace(parm)) p.ParmLib = parm;

                string rep = ReportHlq.Get(context);
                if (!string.IsNullOrWhiteSpace(rep)) p.ReportHlq = rep;

                string inDs = InputDataset.Get(context);
                if (!string.IsNullOrWhiteSpace(inDs)) p.InputDataset = inDs;

                string outSeq = OutputDatasetStaging.Get(context);
                if (!string.IsNullOrWhiteSpace(outSeq)) p.OutputDatasetStaging = outSeq;

                string outIam = OutputDatasetIam.Get(context);
                if (!string.IsNullOrWhiteSpace(outIam)) p.OutputDatasetIam = outIam;

                string defDs = DefinitionDataset.Get(context);
                if (!string.IsNullOrWhiteSpace(defDs)) p.DefinitionDataset = defDs;

                string mapDs = MappingDataset.Get(context);
                if (!string.IsNullOrWhiteSpace(mapDs)) p.MappingDataset = mapDs;

                string iamModel = IamModelDataset.Get(context);
                if (!string.IsNullOrWhiteSpace(iamModel)) p.IamModelDataset = iamModel;

                string iamOwn = IamOwner.Get(context);
                if (!string.IsNullOrWhiteSpace(iamOwn)) p.IamOwner = iamOwn;

                string lrecl = Lrecl.Get(context);
                if (!string.IsNullOrWhiteSpace(lrecl)) p.Lrecl = lrecl;

                string recfm = Recfm.Get(context);
                if (!string.IsNullOrWhiteSpace(recfm)) p.Recfm = recfm;

                string blk = BlkSize.Get(context);
                if (!string.IsNullOrWhiteSpace(blk)) p.BlkSize = blk;

                int commit = CommitFrequency.Get(context);
                if (commit > 0) p.CommitFrequency = commit;

                int prog = ProgressCount.Get(context);
                if (prog > 0) p.ProgressCount = prog;

                string audit = AuditLevel.Get(context);
                if (!string.IsNullOrWhiteSpace(audit)) p.AuditLevel = audit;

                bool waitComplete = WaitForCompletion.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                // Generate full JCL
                string fullJcl = TdmMainframeJclTemplate.GenerateMaskingJcl(p);
                GeneratedJcl.Set(context, fullJcl);

                // Write to temp file and submit
                string tempJclFile = System.IO.Path.GetTempFileName() + ".jcl";
                try
                {
                    System.IO.File.WriteAllText(tempJclFile, fullJcl);

                    string cmdArgs = $"zos-jobs submit local-file \"{tempJclFile}\" --response-format-json";
                    if (waitComplete)
                    {
                        cmdArgs += " --wait-for-active --wait-for-complete";
                    }

                    var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                    IsSuccess.Set(context, execResult.Success);

                    if (execResult.Success)
                    {
                        string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                        try
                        {
                            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                            string jId = token["jobid"]?.ToString() ?? token["data"]?["jobid"]?.ToString() ?? "";
                            string retCode = token["retcode"]?.ToString() ?? token["data"]?["retcode"]?.ToString() ?? "";

                            JobId.Set(context, jId);
                            ReturnCode.Set(context, retCode);
                            SpoolOutput.Set(context, execResult.StdOut);
                        }
                        catch
                        {
                            SpoolOutput.Set(context, execResult.StdOut);
                        }

                        ErrorMessage.Set(context, string.Empty);
                    }
                    else
                    {
                        string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe TDM Masking Job", p.JobName);
                        ErrorMessage.Set(context, formatted);
                        throw new InvalidOperationException(formatted);
                    }
                }
                finally
                {
                    if (System.IO.File.Exists(tempJclFile))
                    {
                        try { System.IO.File.Delete(tempJclFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe TDM Masking Job");
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
