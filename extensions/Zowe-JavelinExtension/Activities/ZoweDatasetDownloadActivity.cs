using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Dataset Download")]
    [Description("Downloads a mainframe Sequential Data Set (PS) or PDS Member into local memory / text variable or local file")]
    public class ZoweDatasetDownloadActivity : ZoweBaseActivity
    {
        [Category("Source Data Set")]
        [RequiredArgument]
        [DisplayName("Data Set Name")]
        [Description("Sequential dataset or PDS member, e.g. 'HLQ.TEST.DATA' or 'HLQ.TEST.COBOL(PROG1)'")]
        public InArgument<string> DatasetName { get; set; }

        [Category("Download Options")]
        [DisplayName("Local File Path")]
        [Description("Optional local path to save the file (e.g. C:\\temp\\prog1.cbl). If empty, stored in TextContent")]
        public InArgument<string> LocalFilePath { get; set; }

        [Category("Download Options")]
        [DisplayName("Binary Transfer")]
        [DefaultValue(false)]
        [Description("If true, downloads in binary mode without EBCDIC to ASCII code page conversion")]
        public InArgument<bool> BinaryTransfer { get; set; }

        [Category("Download Options")]
        [DisplayName("Code Page / Encoding")]
        [Description("Optional encoding/codepage (e.g. IBM-1047, IBM-037, UTF-8)")]
        public InArgument<string> EncodingCodePage { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Text Content")]
        public OutArgument<string> TextContent { get; set; }

        [Category("Output")]
        [DisplayName("Lines DataTable")]
        [Description("DataTable containing LineNumber and Content")]
        public OutArgument<DataTable> LinesDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string ds = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                ds = DatasetName.Get(context);
                string localFile = LocalFilePath.Get(context);
                bool isBinary = BinaryTransfer.Get(context);
                string codePage = EncodingCodePage.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(ds))
                    throw new ArgumentException("Data Set Name não pode ser vazio.");

                string tempFile = null;
                string targetPath = localFile;

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    tempFile = System.IO.Path.GetTempFileName();
                    targetPath = tempFile;
                }

                try
                {
                    string cmdArgs = $"zos-files download data-set \"{ds.Trim()}\" --file \"{targetPath}\"";
                    if (isBinary)
                        cmdArgs += " --binary";
                    if (!string.IsNullOrWhiteSpace(codePage))
                        cmdArgs += $" --encoding \"{codePage.Trim()}\"";

                    var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                    IsSuccess.Set(context, execResult.Success);

                    if (execResult.Success)
                    {
                        string content = "";
                        if (System.IO.File.Exists(targetPath))
                        {
                            content = System.IO.File.ReadAllText(targetPath);
                        }

                        TextContent.Set(context, content);
                        DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(content, "DatasetLines");
                        LinesDataTable.Set(context, dt);
                        ErrorMessage.Set(context, string.Empty);
                    }
                    else
                    {
                        string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Dataset Download", ds);
                        ErrorMessage.Set(context, formatted);
                        throw new InvalidOperationException(formatted);
                    }
                }
                finally
                {
                    if (tempFile != null && System.IO.File.Exists(tempFile))
                    {
                        try { System.IO.File.Delete(tempFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Dataset Download", ds);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
