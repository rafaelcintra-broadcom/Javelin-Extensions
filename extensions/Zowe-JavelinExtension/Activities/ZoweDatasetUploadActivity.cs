using System;
using System.Activities;
using System.ComponentModel;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Dataset Upload")]
    [Description("Uploads a local file, directory, or inline text content into a mainframe Sequential Data Set (PS) or PDS Member")]
    public class ZoweDatasetUploadActivity : ZoweBaseActivity
    {
        [Category("Source & Target")]
        [RequiredArgument]
        [DisplayName("Target Data Set Name")]
        [Description("Target sequential dataset or member, e.g. 'HLQ.TEST.INPUT' or 'HLQ.TEST.COBOL(PROG1)'")]
        public InArgument<string> TargetDatasetName { get; set; }

        [Category("Source & Target")]
        [DisplayName("Local File Path")]
        [Description("Local file to upload (e.g. C:\\data\\sample.txt)")]
        public InArgument<string> LocalFilePath { get; set; }

        [Category("Source & Target")]
        [DisplayName("Inline Text Content")]
        [Description("Direct text content to upload to the mainframe dataset/member")]
        public InArgument<string> InlineTextContent { get; set; }

        [Category("Upload Options")]
        [DisplayName("Binary Transfer")]
        [DefaultValue(false)]
        [Description("If true, uploads binary data without ASCII to EBCDIC translation")]
        public InArgument<bool> BinaryTransfer { get; set; }

        [Category("Upload Options")]
        [DisplayName("Code Page / Encoding")]
        [Description("Optional codepage (e.g. IBM-1047, UTF-8)")]
        public InArgument<string> EncodingCodePage { get; set; }

        // --- Outputs ---

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
                ds = TargetDatasetName.Get(context);
                string localFile = LocalFilePath.Get(context);
                string inlineText = InlineTextContent.Get(context);
                bool isBinary = BinaryTransfer.Get(context);
                string codePage = EncodingCodePage.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(ds))
                    throw new ArgumentException("Target Data Set Name não pode ser vazio.");

                string tempFile = null;
                string sourceFile = localFile;

                if (!string.IsNullOrWhiteSpace(inlineText))
                {
                    tempFile = System.IO.Path.GetTempFileName();
                    System.IO.File.WriteAllText(tempFile, inlineText);
                    sourceFile = tempFile;
                }
                else if (string.IsNullOrWhiteSpace(sourceFile) || !System.IO.File.Exists(sourceFile))
                {
                    throw new ArgumentException("É necessário informar um 'Local File Path' válido ou 'Inline Text Content'.");
                }

                try
                {
                    string cmdArgs = $"zos-files upload file-to-data-set \"{sourceFile}\" \"{ds.Trim()}\"";
                    if (isBinary)
                        cmdArgs += " --binary";
                    if (!string.IsNullOrWhiteSpace(codePage))
                        cmdArgs += $" --encoding \"{codePage.Trim()}\"";

                    var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                    IsSuccess.Set(context, execResult.Success);

                    if (execResult.Success)
                    {
                        ErrorMessage.Set(context, string.Empty);
                    }
                    else
                    {
                        string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Dataset Upload", ds);
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
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Dataset Upload", ds);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
